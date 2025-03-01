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

    public struct Tri
    {
        (Vector3 v, Vector2 uv) a;
        Vector2 uv1;
        Vector3 v2;

        Vector3 v3;
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

        // define a plane to make the cut RELATIVE to meshFilter
        Plane cuttingPlane = new(cutPlaneNormal, cutPositionLocal);
        LastCutInfo.CuttingPlane = cuttingPlane;

        using var _posTris = ListPool<List<(Vector3 v, Vector2 uv)>>.Get(out var positiveUVertTris);
        using var _negTris = ListPool<List<(Vector3 v, Vector2 uv)>>.Get(out var negativeUVertTris);

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            List<(Vector3 vertex, Vector2 uv)> subPosTris = new();
            List<(Vector3 vertex, Vector2 uv)> subNegTris = new();

            var submeshTris = mesh.GetTriangles(i).GroupByTripletsStrict();

            foreach (var tri in submeshTris)
            {
                ArraySegment<(Vector3 position, Vector2 uv)> triUVerts =
                    new(ArrayPool<(Vector3 position, Vector2 uv)>.Shared.Rent(3), 0, 3);

                triUVerts[0] = (verts[tri.a], uvs[tri.a]);
                triUVerts[1] = (verts[tri.b], uvs[tri.b]);
                triUVerts[2] = (verts[tri.c], uvs[tri.c]);

                var a = triUVerts[0];
                var b = triUVerts[1];
                var c = triUVerts[2];

                if (triUVerts.All(uvx => cuttingPlane.GetSide(uvx.position) == true))
                {
                    subPosTris.AddRange(triUVerts);
                }
                else if (triUVerts.All(uvx => cuttingPlane.GetSide(uvx.position) == false))
                {
                    subNegTris.AddRange(triUVerts);
                }
                else
                {
                    Debug.Log(
                        $"triUVerts: {string.Join(", ", triUVerts.Select(t => $"point: {t.position}, side: {cuttingPlane.GetSide(t.position)}"))}"
                    );
                    // inbetween tris to be split by the cut
                    // todo: case where point/s lie on line
                    // three groups -- triangle, trapezium tri a, trapezium tri b

                    var triSegmentsArr = ArrayPool<(
                        (Vector3, Vector2) pointA,
                        (Vector3, Vector2) pointB
                    )>.Shared.Rent(3);
                    triSegmentsArr[0] = (a, b);
                    triSegmentsArr[1] = (b, c);
                    triSegmentsArr[2] = (c, a);

                    using var _i = ListPool<(
                        int segmentIndex,
                        Vector3 intersectPoint,
                        Vector2 uv
                    )>.Get(out var intersections);

                    // find triangle group by seeing which two segments the plane intersects
                    for (int y = 0; y < triSegmentsArr.Length; y++)
                    {
                        var ((aVert, aUv), (bVert, bUv)) = triSegmentsArr[y];

                        if (cuttingPlane.IntersectsSegment(aVert, bVert, out var intersectPoint))
                        {
                            // calculate UV between pointA and pointB
                            // for perf, use dot product to give us normalised distance ratio between A->B and A->intersectPoint
                            var normDistRatio =
                                Vector3.Dot(bVert - aVert, intersectPoint - aVert)
                                / (bVert - aVert).sqrMagnitude;
                            var normDistRatio2 =
                                Vector3.Distance(aVert, intersectPoint)
                                / Vector3.Distance(aVert, bVert);
                            cuttingPlane.Raycast(new(aVert, bVert - aVert), out var dist);
                            var normDistRatio3 = dist / (bVert - aVert).magnitude;
                            var interpolatedUV = Vector2.Lerp(aUv, bUv, normDistRatio);
                            intersections.Add((y, intersectPoint, interpolatedUV));
                        }
                        else
                        {
                            Debug.LogError("cutting plane does not intersect segment!");
                        }
                    }

                    ArrayPool<((Vector3, Vector2) pointA, (Vector3, Vector2) pointB)>.Shared.Return(
                        triSegmentsArr
                    );

                    if (intersections.Count != 2)
                    {
                        Debug.LogError(
                            $"invalid intersections count! expected 2. got {intersections.Count}"
                        );

                        LastCutInfo.Errored = true;
                        for (int j = 0; j < triUVerts.Count; j++)
                        {
                            LastCutInfo.CutTri[j] = triUVerts[j].position;
                            LastCutInfo.TriSides[j] = cuttingPlane.GetSide(triUVerts[j].position);
                        }
                    }

                    // the tri group is the one point shared by the two segments plus the two intersection points.
                    var ((aStart, _), (aEnd, _)) = triSegmentsArr[intersections[0].segmentIndex];
                    var ((bStart, _), (bEnd, _)) = triSegmentsArr[intersections[1].segmentIndex];

                    using var _seg = ListPool<Vector3>.Get(out var segABPoints);
                    segABPoints.Add(aStart);
                    segABPoints.Add(aEnd);
                    segABPoints.Add(bStart);
                    segABPoints.Add(bEnd);

                    // find the shared point
                    var sharedPoint = segABPoints.FindDuplicate();
                    var firstUVert = triUVerts.First(uvx => uvx.position == sharedPoint);

                    ArraySegment<(Vector3 position, Vector2 uv)> currentTriBuffer =
                        new(ArrayPool<(Vector3, Vector2)>.Shared.Rent(3), 0, 3);

                    // shared uvert point from original tri
                    currentTriBuffer[0] = firstUVert;
                    // two intersect uverts
                    currentTriBuffer[1] = (intersections[0].intersectPoint, intersections[0].uv);
                    currentTriBuffer[2] = (intersections[1].intersectPoint, intersections[1].uv);

                    // order in clockwise winding
                    // use cross product to determine winding direction matches original tri
                    var originalTriCross = GetTriNormal(a.position, b.position, c.position);

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triUVerts, currentTriBuffer);
                        break;
                    }

                    var topTriIsPositive = cuttingPlane.GetSide(sharedPoint);
                    (topTriIsPositive ? subPosTris : subNegTris).AddRange(currentTriBuffer);

                    // now the two extra tris from the trapezoid
                    // tri 1: the two intersects stay the same, but use another point from the original tri
                    var secondUVert = triUVerts.SkipWhile(x => x == firstUVert).First();
                    currentTriBuffer[0] = secondUVert;

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triUVerts, currentTriBuffer);
                        break;
                    }

                    // these tris are on the other side compared to the first tri
                    (topTriIsPositive ? subNegTris : subPosTris).AddRange(currentTriBuffer);

                    // tri 2: use the remaining original point, get the intersect this links to, plus the point used before
                    var thirdUVert = triUVerts
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
                    currentTriBuffer[1] = (intersectPoint3.intersectPoint, intersectPoint3.uv);
                    currentTriBuffer[2] = secondUVert;

                    if (!EnsureCrossMatch(currentTriBuffer, originalTriCross))
                    {
                        cuttingPlane = DebugCrossMatch(cuttingPlane, triUVerts, currentTriBuffer);
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
        posSideMesh.SetVertices(flattenedUVertsPos.Select(uvert => uvert.v).ToArray());
        posSideMesh.SetUVs(0, flattenedUVertsPos.Select(uvert => uvert.uv).ToArray());

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
        negSideMesh.SetVertices(flattenedUVertsNeg.Select(uvert => uvert.v).ToArray());
        negSideMesh.SetUVs(0, flattenedUVertsNeg.Select(uvert => uvert.uv).ToArray());

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
            ArraySegment<(Vector3 position, Vector2 uv)> triUVerts,
            ArraySegment<(Vector3 position, Vector2 uv)> currentTriBuffer
        )
        {
            LastCutInfo.Errored = true;
            for (int j = 0; j < triUVerts.Count; j++)
            {
                LastCutInfo.CutTri[j] = triUVerts[j].position;
                LastCutInfo.TriSides[j] = cuttingPlane.GetSide(triUVerts[j].position);
            }
            LastCutInfo.SubCutTri.AddRange(currentTriBuffer.Select(uvx => uvx.position));

            return cuttingPlane;
        }
    }

    public static Vector3 GetTriNormal(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(b - a, c - a);

    public static bool EnsureCrossMatch(IList<(Vector3, Vector2)> uverts, Vector3 crossVec)
    {
        var a = uverts[0];
        var b = uverts[1];
        var c = uverts[2];

        var firstTryCross = GetTriNormal(uverts[0].Item1, uverts[1].Item1, uverts[2].Item1);
        if (Vector3.Dot(firstTryCross, crossVec) > 0)
        {
            return true;
        }

        uverts[1] = c;
        uverts[2] = b;

        var secondTryCross = GetTriNormal(uverts[0].Item1, uverts[1].Item1, uverts[2].Item1);
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
