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
        public List<VData> CutEdgeVerts = new();

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
            CutEdgeVerts.Clear();
        }
    }

    public readonly struct VData : IEquatable<VData>
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
            thisVData.VertexPosition.Approximately(other.VertexPosition)
            && thisVData.UV.Approximately(other.UV)
            && thisVData.Normal.Approximately(other.Normal);

        public static bool operator !=(VData thisVData, VData other) =>
            !thisVData.VertexPosition.Approximately(other.VertexPosition)
            || !thisVData.UV.Approximately(other.UV)
            || !thisVData.Normal.Approximately(other.Normal);

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

        public bool Equals(VData other) => this == other;

        // override object.GetHashCode
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Helpers.Quantize(VertexPosition),
                Helpers.Quantize(UV),
                Helpers.Quantize(Normal)
            );
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

            using var _cev = ListPool<VData>.Get(out var cutEdgeVerts);

            var submeshTris = mesh.GetTriangles(i).GroupByTripletsStrict();

            using var _tb = ArrayPool<VData>.Shared.GetPooledSegment(3, out var triBuffer);
            LinkedList<VData> processedEdgeVerts = new();
            foreach (var (a, b, c) in submeshTris)
            {
                triBuffer[0] = GetVData(a);
                triBuffer[1] = GetVData(b);
                triBuffer[2] = GetVData(c);

                using var _s = ArrayPool<bool>.Shared.GetPooledSegment(3, out var sides);

                for (int j = 0; j < triBuffer.Count; j++)
                {
                    sides[j] = cuttingPlane.GetSide(triBuffer[j].VertexPosition);
                }

                if (sides.All(x => x == true))
                {
                    subPosTris.AddRange(triBuffer);
                }
                else if (sides.All(x => x == false))
                {
                    subNegTris.AddRange(triBuffer);
                }
                // todo edge case: one pos, one neg, one sits on plane
                // this tri can be split into two tris instead of 3
                // todo edge case: two sits on plane
                // this tri can be sorted into the side that the non-intersect vert is on
                // todo edge case: 3 sits on plane
                // may as well default to positive side or something.
                // todo if verts sit on the plane, this may impact our fill-step later
                else
                {
                    // when you have a plane intersecting a tri, it will be one vert on one side and two verts on the other side, unless some verts sit exactly on the plane
                    // X: single point on one side of the plane
                    // Y1 & Y2: the two points on the other side of the plane.
                    // then plane raycast from X to Y1/Y2 to get intersects I1 & I2, corresponding to Y1 & Y2
                    // then we can always define our generated tris by (X I1 I2), (Y1 I1 I2), (Y2 Y1 I2)

                    // Debug.Log(
                    //     $"triUVerts: {string.Join(", ", triBuffer.Select(t => $"point: {t.VertexPosition}, side: {cuttingPlane.GetSide(t.VertexPosition)}"))}"
                    // );

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

                    cutEdgeVerts.Add(i1);
                    cutEdgeVerts.Add(i2);
                }
            }

            if (cutEdgeVerts.Any())
            {
                // process the fill for the open face left by the cut
                // todo if the cut would create multiple open faces, need to change algorithm and potentially create more than 2 meshes
                var midPoint =
                    cutEdgeVerts.Aggregate(Vector3.zero, (accum, v) => accum += v.VertexPosition)
                    / cutEdgeVerts.Count;

                processedEdgeVerts.Clear();
                var first = processedEdgeVerts.AddFirst(cutEdgeVerts[0]);
                var second = processedEdgeVerts.AddLast(cutEdgeVerts[1]);

                using var _pp = HashSetPool<(VData, VData)>.Get(out var processedPairs);

                bool anyLink = false;

                void CheckPair(VData v1, VData v2)
                {
                    if (processedPairs.Contains((v1, v2)))
                        return;

                    if (processedEdgeVerts.First.Value.VertexPosition == v1.VertexPosition)
                    {
                        processedEdgeVerts.AddFirst(v2);
                        processedPairs.Add((v2, v1));
                        anyLink = true;
                    }
                    else if (processedEdgeVerts.Last.Value.VertexPosition == v1.VertexPosition)
                    {
                        processedEdgeVerts.AddLast(v2);
                        processedPairs.Add((v1, v2));
                        anyLink = true;
                    }
                }
                do
                {
                    anyLink = false;
                    // evaluate pairs so that our edge vertices are ordered
                    for (int j = 2; j < cutEdgeVerts.Count; j += 2)
                    {
                        var a = cutEdgeVerts[j];
                        var b = cutEdgeVerts[j + 1];

                        CheckPair(a, b);
                        CheckPair(b, a);
                    }
                } while (anyLink);

                // for each element in vData, create tri with next element and midpoint
                for (var node = processedEdgeVerts.First; node != null; node = node.Next)
                {
                    VData v1 = node.Value;
                    VData v2 = (node.Next ?? processedEdgeVerts.First).Value;

                    Vector2 uv = new(0.5f, 0.5f);

                    triBuffer[0] = new(v1.VertexPosition, uv, -cutPlaneNormal);
                    triBuffer[1] = new(midPoint, uv, -cutPlaneNormal);
                    triBuffer[2] = new(v2.VertexPosition, uv, -cutPlaneNormal);
                    EnsureCrossMatch(triBuffer, -cutPlaneNormal);

                    subPosTris.AddRange(triBuffer);

                    // reverse plane
                    triBuffer[0] = new(v1.VertexPosition, uv, cutPlaneNormal);
                    triBuffer[1] = new(midPoint, uv, cutPlaneNormal);
                    triBuffer[2] = new(v2.VertexPosition, uv, cutPlaneNormal);
                    EnsureCrossMatch(triBuffer, cutPlaneNormal);

                    subNegTris.AddRange(triBuffer);
                }
            }

            positiveUVertTris.Add(subPosTris);
            negativeUVertTris.Add(subNegTris);
        }
        // todo fill

        // create two meshes now with the positive and negative side vertices
        // Debug.Log(
        //     $"original count: {mesh.triangles.Length}, pos count: {positiveUVertTris[0].Count}, neg count: {negativeUVertTris[0].Count}, total: {positiveUVertTris[0].Count + negativeUVertTris[0].Count}"
        // );

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

        // negative side
        Mesh negSideMesh = GetMesh(negativeUVertTris);

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
