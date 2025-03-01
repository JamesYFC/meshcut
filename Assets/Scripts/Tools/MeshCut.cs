using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;

public static class MeshCut
{
    public class CutDebugInfo
    {
        public bool Errored = false;
        public Plane CuttingPlane;
        public Vector3[] CutTri = new Vector3[3];
        public bool[] TriSides = new bool[3];
        public List<Vector3> SubCutTri = new();
        public (Vector3 originalCross, Vector3 newCross, Vector3 newCrossFlipped) CrossData;

        public void Reset()
        {
            Errored = false;
            CuttingPlane = default;

            for (int i = 0; i < CutTri.Length; i++)
            {
                CutTri[i] = default;
            }

            for (int i = 0; i < TriSides.Length; i++)
            {
                TriSides[i] = default;
            }

            SubCutTri.Clear();
            CrossData = default;
        }
    }

    public readonly struct VData
    {
        public readonly Vector3 VertexPosition;
        public readonly Vector2 UV;
        public readonly Vector3 Normal;

        public VData(Vector3 pos, Vector2 uv, Vector3 normal)
        {
            VertexPosition = pos;
            UV = uv;
            Normal = normal;
        }

        public static bool operator ==(VData thisVData, VData other) =>
            thisVData.VertexPosition == other.VertexPosition
            && thisVData.UV == other.UV
            && thisVData.Normal == other.Normal;

        public static bool operator !=(VData thisVData, VData other) =>
            thisVData.VertexPosition != other.VertexPosition
            || thisVData.UV != other.UV
            || thisVData.Normal != other.Normal;

        // override object.Equals
        public override bool Equals(object obj)
        {
            if (obj is VData vData)
            {
                return this == vData;
            }
            else
            {
                return false;
            }
        }

        // override object.GetHashCode
        public override int GetHashCode()
        {
            return HashCode.Combine(VertexPosition, UV, Normal);
        }

        public override readonly string ToString() => (VertexPosition, UV, Normal).ToString();

        public readonly void Deconstruct(out Vector3 vertexPos, out Vector2 uv, out Vector3 normal)
        {
            vertexPos = VertexPosition;
            uv = UV;
            normal = Normal;
        }
    }

    public static CutDebugInfo LastCutInfo = new();

    public static (Mesh above, Mesh below) CutMesh(
        MeshFilter meshFilter,
        Vector3 cutPositionLocal,
        Vector3 cutPlaneNormal
    )
    {
        LastCutInfo.Reset();

        Mesh mesh = meshFilter.mesh;
        using var _v = ListPool<Vector3>.Get(out var verts);
        using var _n = ListPool<Vector3>.Get(out var normals);
        using var _uv = ListPool<Vector2>.Get(out var uvs);
        mesh.GetVertices(verts);
        mesh.GetNormals(normals);
        mesh.GetUVs(0, uvs);

        VData GetVData(int index) => new(verts[index], uvs[index], normals[index]);

        // define a plane to make the cut RELATIVE to meshFilter
        Plane cuttingPlane = new(cutPlaneNormal, cutPositionLocal);
        LastCutInfo.CuttingPlane = cuttingPlane;

        using var _posTris = ListPool<List<VData>>.Get(out var positiveUVertTris);
        using var _negTris = ListPool<List<VData>>.Get(out var negativeUVertTris);

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            List<VData> subPosTris = new();
            List<VData> subNegTris = new();

            var submeshTris = mesh.GetTriangles(i).GroupByTripletsStrict();

            foreach (var (a, b, c) in submeshTris)
            {
                using var _tb = ArrayPool<VData>.Shared.GetPooledSegment(3, out var triBuffer);

                triBuffer[0] = GetVData(a);
                triBuffer[1] = GetVData(b);
                triBuffer[2] = GetVData(c);

                var sides = triBuffer.Select(x => cuttingPlane.GetSide(x.VertexPosition)).ToArray();

                if (sides.All(x => x == true))
                {
                    subPosTris.AddRange(triBuffer);
                }
                else if (sides.All(x => x == false))
                {
                    subNegTris.AddRange(triBuffer);
                }
                else
                {
                    // sort into X, Y1, Y2
                    // then just do the plane raycast from X to Y1/Y2 in order to keep intersect order I1 I2 matching Y1 Y2
                    // then we can simply go (X I1 I2), (Y1 I1 I2), (Y2 Y1 I2)
                    // Debug.Log(
                    //     $"triUVerts: {string.Join(", ", triBuffer.Select(t => $"point: {t.VertexPosition}, side: {cuttingPlane.GetSide(t.VertexPosition)}"))}"
                    // );

                    // x = tri point that makes the 1-triangle group
                    // y1 & y2 = other two tri points that lie on the other side of the plane, that make two additional tris with the two intersects from them to x
                    VData x,
                        y1,
                        y2;

                    // positive/negative sides
                    using var _pd = DictionaryPool<int, VData>.Get(out var processingDict);

                    int smallerSign = 0;

                    // sorting vert data into pos/negative sides. so if 2 pos 1 neg, it will look like {-1: x, 1: y1, 2: y2}
                    for (int j = 0; j < 3; j++)
                    {
                        int sign = sides[j] ? 1 : -1;
                        VData vertData = triBuffer[j];

                        if (!processingDict.TryAdd(sign, vertData))
                        {
                            smallerSign = sign * -1;
                            processingDict[sign * 2] = vertData;
                        }
                    }

                    // smaller sign will only have 1 entry, therefore that is X
                    x = processingDict[smallerSign];
                    y1 = processingDict[smallerSign * -1];
                    y2 = processingDict[smallerSign * -2];

                    // now get intersects I1 and I2. I1 corresponds to the intersect point between X and Y1, same thing for I2 and X / Y2
                    VData GetIntersect(VData y)
                    {
                        var (xPos, _, _) = x;
                        var (yPos, _, _) = y;

                        Vector3 xToY = yPos - xPos;

                        Ray ray = new(xPos, xToY);

                        if (!cuttingPlane.Raycast(ray, out float dist))
                        {
                            Debug.LogError($"intersect not found from {xPos} to {yPos}");
                            return default;
                        }

                        var intersectPos = ray.GetPoint(dist);

                        // get normalised dist ratio of the intersect vs total distance from X to y
                        var normDistRatio =
                            Vector3.Dot(xToY, intersectPos - xPos) / xToY.sqrMagnitude;

                        var interpolatedUV = Vector2.Lerp(x.UV, y.UV, normDistRatio);
                        var interpolatedNormal = Vector3.Lerp(x.Normal, y.Normal, normDistRatio);

                        return new(intersectPos, interpolatedUV, interpolatedNormal);
                    }

                    var i1 = GetIntersect(y1);
                    var i2 = GetIntersect(y2);

                    // now we're ready to form our triangles. take the original cross product to check for winding order later
                    var originalTriCross = GetTriNormal(triBuffer);

                    // top triangle group of the cut: x,i2,i1
                    triBuffer[0] = x;
                    triBuffer[1] = i2;
                    triBuffer[2] = i1;

                    if (!EnsureCrossMatch(triBuffer, originalTriCross))
                        Debug.LogError("crossMatch failed");

                    (smallerSign > 0 ? subPosTris : subNegTris).AddRange(triBuffer);

                    // bottom two
                    var botTrisDest = smallerSign > 0 ? subNegTris : subPosTris;

                    // first bot tri: y1, i1, i2
                    triBuffer[0] = y1;
                    triBuffer[1] = i1;
                    triBuffer[2] = i2;
                    if (!EnsureCrossMatch(triBuffer, originalTriCross))
                        Debug.LogError("crossMatch failed");
                    botTrisDest.AddRange(triBuffer);

                    // second bot tri: y2, y1, i2
                    triBuffer[0] = y2;
                    triBuffer[1] = y1;
                    triBuffer[2] = i2;
                    if (!EnsureCrossMatch(triBuffer, originalTriCross))
                        Debug.LogError("crossMatch failed");
                    botTrisDest.AddRange(triBuffer);
                }
            }

            positiveUVertTris.Add(subPosTris);
            negativeUVertTris.Add(subNegTris);
        }
        // todo fill

        // create two meshes now with the positive and negative side vertices
        Debug.Log(
            $"original count: {mesh.triangles.Length}, pos count: {positiveUVertTris[0].Count}, neg count: {negativeUVertTris[0].Count}, total: {positiveUVertTris[0].Count + negativeUVertTris[0].Count}"
        );

        Mesh GetMesh(List<List<VData>> vData)
        {
            Mesh mesh = new();
            using var _v = ListPool<Vector3>.Get(out var vertices);
            using var _uv = ListPool<Vector2>.Get(out var uvs);
            using var _n = ListPool<Vector3>.Get(out var normals);
            // use a dict to track already set vertices
            using var _d = DictionaryPool<VData, int>.Get(out var vDataIndices);

            using var _tbs = ListPool<int[]>.Get(out var trisBySubmesh);

            for (int i = 0; i < vData.Count; i++)
            {
                List<VData> submesh = vData[i];
                var submeshTris = new int[submesh.Count];
                for (int j = 0; j < submesh.Count; j++)
                {
                    VData vd = submesh[j];
                    if (!vDataIndices.TryGetValue(vd, out var index))
                    {
                        // if we haven't added a vert yet, do it now
                        vertices.Add(vd.VertexPosition);
                        uvs.Add(vd.UV);
                        normals.Add(vd.Normal);

                        // index is the last vert
                        index = vertices.Count - 1;
                        vDataIndices[vd] = index;
                    }

                    submeshTris[j] = index;
                }
                trisBySubmesh.Add(submeshTris);
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);

            for (int i = 0; i < trisBySubmesh.Count; i++)
            {
                int[] tris = trisBySubmesh[i];
                mesh.SetTriangles(tris, i);
            }

            return mesh;
        }

        // positive side
        Mesh posSideMesh = GetMesh(positiveUVertTris);

        if (posSideMesh.normals.Any(n => n == Vector3.zero))
            Debug.LogError("zero!");
        //posSideMesh.Optimize();
        //posSideMesh.RecalculateNormals();
        if (posSideMesh.normals.Any(n => n == Vector3.zero))
            Debug.LogError("zero postRecalc!");

        // negative side
        Mesh negSideMesh = GetMesh(negativeUVertTris);

        if (negSideMesh.normals.Any(n => n == Vector3.zero))
            Debug.LogError("zero!");
        //negSideMesh.Optimize();
        //negSideMesh.RecalculateNormals();
        if (negSideMesh.normals.Any(n => n == Vector3.zero))
            Debug.LogError("zero postRecalc!");
        return (posSideMesh, negSideMesh);
    }

    public static Vector3 GetTriNormal(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(b - a, c - a);

    public static Vector3 GetTriNormal(IList<VData> tri) =>
        Vector3.Cross(
            tri[1].VertexPosition - tri[0].VertexPosition,
            tri[2].VertexPosition - tri[0].VertexPosition
        );

    public static bool EnsureCrossMatch(IList<VData> uverts, Vector3 crossVec)
    {
        var a = uverts[0];
        var b = uverts[1];
        var c = uverts[2];

        var firstTryCross = GetTriNormal(uverts);
        if (Vector3.Dot(firstTryCross, crossVec) > 0)
        {
            return true;
        }

        uverts[1] = c;
        uverts[2] = b;

        var secondTryCross = GetTriNormal(uverts);
        if (Vector3.Dot(secondTryCross, crossVec) < 0)
        {
            Debug.LogError(
                $"CrossMatch failed! original: {crossVec}. first try: {firstTryCross}. after flipping: {secondTryCross}"
            );
            LastCutInfo.CrossData = (crossVec, firstTryCross, secondTryCross);

            return false;
        }

        return true;
    }
}
