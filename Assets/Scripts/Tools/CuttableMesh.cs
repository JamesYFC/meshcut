using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class CuttableMesh : MonoBehaviour
{
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;
    public MeshCollider meshCollider;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
    }

    void OnDrawGizmos()
    {
        if (!MeshCut.LastCutInfo.Errored)
        {
            return;
        }

        var cutInfo = MeshCut.LastCutInfo;
        var cutPlane = cutInfo.CuttingPlane;
        var cutTri = cutInfo.CutTri;
        var subCutTri = cutInfo.SubCutTri;

        Debug.Log(
            $"meshCut errored with plane norm: {cutPlane.normal} dist: {cutPlane.distance} | tri {string.Join(", ", cutTri)}"
        );

        // draw plane
        DrawPlane(cutInfo.CuttingPlane.normal, cutInfo.CuttingPlane.distance, 1);

        // draw tri
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(cutTri[0], cutTri[1]);
        Gizmos.DrawLine(cutTri[1], cutTri[2]);
        Gizmos.DrawLine(cutTri[2], cutTri[0]);
        // draw verts above/below
        // for (int i = 0; i < lastCutTri.Length; i++)
        // {
        //     Gizmos.color = lastCutSide[i] ? Color.green : Color.red;
        //     Gizmos.DrawSphere(lastCutTri[i], .01f);
        // }
        // draw sub-tris
        foreach (var (a, b, c) in cutInfo.SubCutTri.GroupByTripletsStrict())
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }

        if (cutInfo.CrossData != default)
        {
            var (origCr, try1Cr, try2Cr) = cutInfo.CrossData;

            // draw original cross result
            Gizmos.color = Color.white;
            var origMidPoint = (cutTri[0] + cutTri[1] + cutTri[2]) / 3;
            Gizmos.DrawLine(origMidPoint, origMidPoint + origCr);

            // draw subcut cross result
            Gizmos.color = Color.blue;
            var subMidPoint = (subCutTri[0] + subCutTri[1] + subCutTri[2]) / 3;
            Gizmos.DrawLine(subMidPoint, subMidPoint + try1Cr);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(subMidPoint, subMidPoint + try2Cr);
        }

        if (cutInfo.CutEdgeVerts.Count > 0)
        {
            Gizmos.color = Color.black;

            for (int i = 0; i < cutInfo.CutEdgeVerts.Count; i++)
            {
                MeshCut.VData v = cutInfo.CutEdgeVerts[i];
                Handles.Label(v.VertexPosition + (i * 0.1f * Vector3.up), (i + 1).ToString());
            }
        }
    }

    void DrawPlane(Vector3 normal, float distance, float size)
    {
        // Find a point on the plane
        Vector3 center = -normal * distance;

        // Compute two perpendicular vectors (right & forward) on the plane
        Vector3 right = Vector3.Cross(normal, Vector3.forward).normalized;
        if (right == Vector3.zero)
            right = Vector3.Cross(normal, Vector3.right).normalized;

        Vector3 forward = Vector3.Cross(right, normal).normalized;

        // Half-size
        Vector3 halfRight = right * (size / 2);
        Vector3 halfForward = forward * (size / 2);

        // Compute the four corners
        Vector3 topLeft = center + halfForward - halfRight;
        Vector3 topRight = center + halfForward + halfRight;
        Vector3 bottomLeft = center - halfForward - halfRight;
        Vector3 bottomRight = center - halfForward + halfRight;

        // Draw the plane edges
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);

        // Draw normal
        Gizmos.color = Color.red;
        Gizmos.DrawRay(center, normal * size / 2); // Visualize the normal
    }
}
