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

    public struct VData
    {
        public Vector3 VertexPosition;
        public Vector2 UV;
        public Vector3 Normal;

        public static bool operator ==(VData thisVData, VData other) =>
            thisVData.VertexPosition == other.VertexPosition
            && thisVData.UV == other.UV
            && thisVData.Normal == other.Normal;

        public static bool operator !=(VData thisVData, VData other) =>
            thisVData.VertexPosition != other.VertexPosition
            || thisVData.UV != other.UV
            || thisVData.Normal != other.Normal;

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

        VData GetVData(int index) =>
            new()
            {
                VertexPosition = verts[index],
                UV = uvs[index],
                Normal = normals[index]
            };

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

            foreach (var tri in submeshTris)
            {
                ArraySegment<VData> triVData = new(ArrayPool<VData>.Shared.Rent(3), 0, 3);

                triVData[0] = GetVData(tri.a);
                triVData[1] = GetVData(tri.b);
                triVData[2] = GetVData(tri.c);

                var a = triVData[0];
                var b = triVData[1];
                var c = triVData[2];

                if (triVData.All(uvx => cuttingPlane.GetSide(uvx.VertexPosition) == true))
                {
                    subPosTris.AddRange(triVData);
                }
                else if (triVData.All(uvx => cuttingPlane.GetSide(uvx.VertexPosition) == false))
                {
                    subNegTris.AddRange(triVData);
                }
                else
                {
                    Debug.Log(
                        $"triUVerts: {string.Join(", ", triVData.Select(t => $"point: {t.VertexPosition}, side: {cuttingPlane.GetSide(t.VertexPosition)}"))}"
                    );
                    // inbetween tris to be split by the cut
                    // todo: case where point/s lie on line
                    // three groups -- triangle, trapezium tri a, trapezium tri b

                    var triSegmentsArr = ArrayPool<(VData pointA, VData pointB)>.Shared.Rent(3);
                    triSegmentsArr[0] = (a, b);
                    triSegmentsArr[1] = (b, c);
                    triSegmentsArr[2] = (c, a);

                    using var _i = ListPool<(int segmentIndex, VData vData)>.Get(
                        out var intersections
                    );

                    // find triangle group by seeing which two segments the plane intersects
                    for (int y = 0; y < triSegmentsArr.Length; y++)
                    {
                        var ((aVert, aUv, aN), (bVert, bUv, bN)) = triSegmentsArr[y];

                        if (cuttingPlane.IntersectsSegment(aVert, bVert, out var intersectPoint))
                        {
                            // calculate UV between pointA and pointB
                            // for perf, use dot product to give us normalised distance ratio between A->B and A->intersectPoint
                            var normDistRatio =
                                Vector3.Dot(bVert - aVert, intersectPoint - aVert)
                                / (bVert - aVert).sqrMagnitude;
                            var interpolatedUV = Vector2.Lerp(aUv, bUv, normDistRatio);
                            var interpolatedNormal = Vector3.Lerp(aN, bN, normDistRatio);
                            intersections.Add(
                                (
                                    y,
                                    new()
                                    {
                                        VertexPosition = intersectPoint,
                                        UV = interpolatedUV,
                                        Normal = interpolatedNormal
                                    }
                                )
                            );
                        }
                        else
                        {
                            Debug.LogError("cutting plane does not intersect segment!");
                        }
                    }

                    ArrayPool<(VData pointA, VData pointB)>.Shared.Return(triSegmentsArr);

                    if (intersections.Count != 2)
                    {
                        Debug.LogError(
                            $"invalid intersections count! expected 2. got {intersections.Count}"
                        );

                        LastCutInfo.Errored = true;
                        for (int j = 0; j < triVData.Count; j++)
                        {
                            LastCutInfo.CutTri[j] = triVData[j].VertexPosition;
                            LastCutInfo.TriSides[j] = cuttingPlane.GetSide(
                                triVData[j].VertexPosition
                            );
                        }
                    }

                    // the tri group is the one point shared by the two segments plus the two intersection points.
                    var ((aStart, _, _), (aEnd, _, _)) = triSegmentsArr[
                        intersections[0].segmentIndex
                    ];
                    var ((bStart, _, _), (bEnd, _, _)) = triSegmentsArr[
                        intersections[1].segmentIndex
                    ];

                    using var _seg = ListPool<Vector3>.Get(out var segABPoints);
                    segABPoints.Add(aStart);
                    segABPoints.Add(aEnd);
                    segABPoints.Add(bStart);
                    segABPoints.Add(bEnd);

                    // find the shared point
                    var sharedPoint = segABPoints.FindDuplicate();
                    var firstUVert = triVData.First(uvx => uvx.VertexPosition == sharedPoint);

                    ArraySegment<VData> currentTriBuffer =
                        new(ArrayPool<VData>.Shared.Rent(3), 0, 3);

                    // shared uvert point from original tri
                    currentTriBuffer[0] = firstUVert;
                    // two intersect uverts
                    currentTriBuffer[1] = intersections[0].vData;
                    currentTriBuffer[2] = intersections[1].vData;

                    // order in clockwise winding
                    // use cross product to determine winding direction matches original tri
                    var originalTriCross = GetTriNormal(
                        a.VertexPosition,
                        b.VertexPosition,
                        c.VertexPosition
                    );

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triVData, currentTriBuffer);
                        break;
                    }

                    var topTriIsPositive = cuttingPlane.GetSide(sharedPoint);
                    (topTriIsPositive ? subPosTris : subNegTris).AddRange(currentTriBuffer);

                    // now the two extra tris from the trapezoid
                    // tri 1: the two intersects stay the same, but use another point from the original tri
                    var secondUVert = triVData.SkipWhile(x => x == firstUVert).First();
                    currentTriBuffer[0] = secondUVert;

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triVData, currentTriBuffer);
                        break;
                    }

                    // these tris are on the other side compared to the first tri
                    (topTriIsPositive ? subNegTris : subPosTris).AddRange(currentTriBuffer);

                    // tri 2: use the remaining original point, get the intersect this links to, plus the point used before
                    var thirdUVert = triVData
                        .Where(uvx => uvx != firstUVert && uvx != secondUVert)
                        .Single();

                    // get the segment between this and uvx1
                    var intersectPoint3 = intersections.Find(i =>
                    {
                        var (pointA, pointB) = triSegmentsArr[i.segmentIndex];
                        return (pointA == thirdUVert && pointB == firstUVert)
                            || (pointA == firstUVert && pointB == thirdUVert);
                    });

                    currentTriBuffer[0] = thirdUVert;
                    currentTriBuffer[1] = intersectPoint3.vData;
                    currentTriBuffer[2] = secondUVert;

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triVData, currentTriBuffer);
                        break;
                    }

                    (topTriIsPositive ? subNegTris : subPosTris).AddRange(currentTriBuffer);
                }
            }

            positiveUVertTris.Add(subPosTris);
            negativeUVertTris.Add(subNegTris);
        }
        // todo fill

        // create two meshes now with the positive and negative side vertices
        Debug.Log($"pos count: {positiveUVertTris.Count}, neg count: {negativeUVertTris.Count}");

        // positive side
        Mesh posSideMesh = new();
        var flattenedUVertsPos = positiveUVertTris.SelectMany(x => x).ToList();
        posSideMesh.SetVertices(flattenedUVertsPos.Select(uvert => uvert.VertexPosition).ToArray());
        posSideMesh.SetUVs(0, flattenedUVertsPos.Select(uvert => uvert.UV).ToArray());

        // set tris for positive side
        for (int i = 0; i < positiveUVertTris.Count; i++)
        {
            var thisSubmeshUVerts = positiveUVertTris[i];
            // convert uverts to indices
            var thisSubmeshTris = thisSubmeshUVerts
                .Select(uvert => flattenedUVertsPos.FindIndex(cuv => cuv == uvert))
                .ToArray();
            posSideMesh.SetTriangles(thisSubmeshTris, i);
        }
        posSideMesh.RecalculateNormals();

        // negative side
        Mesh negSideMesh = new();
        var flattenedUVertsNeg = negativeUVertTris.SelectMany(x => x).ToList();
        negSideMesh.SetVertices(flattenedUVertsNeg.Select(uvert => uvert.VertexPosition).ToArray());
        negSideMesh.SetUVs(0, flattenedUVertsNeg.Select(uvert => uvert.UV).ToArray());

        // set tris for negative side
        for (int i = 0; i < negativeUVertTris.Count; i++)
        {
            var thisSubmeshUVerts = negativeUVertTris[i];
            // convert uverts to indices
            var thisSubmeshTris = thisSubmeshUVerts
                .Select(uvert => flattenedUVertsNeg.FindIndex(cuv => cuv == uvert))
                .ToArray();
            negSideMesh.SetTriangles(thisSubmeshTris, i);
        }
        negSideMesh.RecalculateNormals();

        return (posSideMesh, negSideMesh);

        static Plane DebugCrossMatch(
            Plane cuttingPlane,
            ArraySegment<VData> triUVerts,
            ArraySegment<VData> currentTriBuffer
        )
        {
            LastCutInfo.Errored = true;
            for (int j = 0; j < triUVerts.Count; j++)
            {
                LastCutInfo.CutTri[j] = triUVerts[j].VertexPosition;
                LastCutInfo.TriSides[j] = cuttingPlane.GetSide(triUVerts[j].VertexPosition);
            }
            LastCutInfo.SubCutTri.AddRange(currentTriBuffer.Select(uvx => uvx.VertexPosition));

            return cuttingPlane;
        }
    }

    public static Vector3 GetTriNormal(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(b - a, c - a);

    public static bool EnsureCrossMatch(IList<VData> uverts, Vector3 crossVec)
    {
        var a = uverts[0];
        var b = uverts[1];
        var c = uverts[2];

        var firstTryCross = GetTriNormal(
            uverts[0].VertexPosition,
            uverts[1].VertexPosition,
            uverts[2].VertexPosition
        );
        if (Vector3.Dot(firstTryCross, crossVec) > 0)
        {
            return true;
        }

        uverts[1] = c;
        uverts[2] = b;

        var secondTryCross = GetTriNormal(
            uverts[0].VertexPosition,
            uverts[1].VertexPosition,
            uverts[2].VertexPosition
        );
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
