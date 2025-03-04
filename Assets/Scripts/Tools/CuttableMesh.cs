using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class CuttableMesh : MonoBehaviour
{
    public Transform debugTr;

    InputAction clickAction;
    InputAction pointerPosAction;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    void Start()
    {
        clickAction = InputSystem.actions.FindAction("Attack");
        pointerPosAction = InputSystem.actions.FindAction("PointerPos");

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
    }

    void Update()
    {
        if (clickAction.WasPerformedThisFrame())
        {
            var screenPoint = pointerPosAction.ReadValue<Vector2>();

            // place the pointer position on the same plane as target object in terms of distance from camera viewpoint
            Plane plane = new(Camera.main.transform.forward, transform.position);

            var ray = Camera.main.ScreenPointToRay(screenPoint);
            plane.Raycast(ray, out float dist);
            var pointerPos = ray.GetPoint(dist);

            // get local position for CutMesh
            var relPoint = transform.InverseTransformPoint(pointerPos);
            // for now always have direction be towards my position
            var towardsDir = transform.InverseTransformDirection(transform.position - pointerPos);

            // use cross product (right hand rule) to get cut normal for use in plane
            var perpendicularDir = -Camera.main.transform.forward;
            var cutNormal = Vector3.Cross(towardsDir, perpendicularDir).normalized;

            if (debugTr)
            {
                debugTr.position = relPoint;
                debugTr.rotation = Quaternion.LookRotation(cutNormal);
            }

            var (mesh1, mesh2) = MeshCut.CutMesh(meshFilter, relPoint, cutNormal);

            meshFilter.mesh = mesh1;
            meshCollider.sharedMesh = mesh1;

            GameObject newPart =
                new(
                    "NewPart",
                    typeof(MeshFilter),
                    typeof(MeshRenderer),
                    typeof(MeshCollider)
                //typeof(Rigidbody)
                );
            var newMeshFilter = newPart.GetComponent<MeshFilter>();
            var newMeshRenderer = newPart.GetComponent<MeshRenderer>();
            var newMeshCollider = newPart.GetComponent<MeshCollider>();

            newMeshFilter.mesh = mesh2;
            newMeshRenderer.materials = meshRenderer.materials;
            newMeshCollider.convex = true;
            newMeshCollider.sharedMesh = mesh2;
        }
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
        var cutSide = cutInfo.TriSides;
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
        foreach (var v in cutInfo.SubCutTri.GroupByTripletsStrict())
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(v.a, v.b);
            Gizmos.DrawLine(v.b, v.c);
            Gizmos.DrawLine(v.c, v.a);
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
