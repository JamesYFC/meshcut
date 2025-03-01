using UnityEngine;

public class MeshSplitter : MonoBehaviour
{
    // Original triangle vertices
    public Vector3 v1 = new Vector3(0, 0, 0);
    public Vector3 v2 = new Vector3(1, 0, 0);
    public Vector3 v3 = new Vector3(0, 1, 0);

    // Line intersection points inside the triangle (you can set these manually or calculate dynamically)
    public Vector3 P1 = new Vector3(0.5f, 0.25f, 0); // Intersection point on v1-v2 edge
    public Vector3 P2 = new Vector3(0.5f, 0.75f, 0); // Intersection point on v2-v3 edge

    // For visualization: split the triangle into two parts
    private void OnDrawGizmos()
    {
        // Draw the original triangle
        Gizmos.color = Color.white;
        Gizmos.DrawLine(v1, v2);
        Gizmos.DrawLine(v2, v3);
        Gizmos.DrawLine(v3, v1);

        // Draw the splitting line (yellow) that intersects the triangle at P1 and P2
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(P1, P2);

        // Draw the first smaller triangle formed by splitting the original triangle (Red)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(v1, P1);
        Gizmos.DrawLine(P1, P2);
        Gizmos.DrawLine(P2, v1);

        // Draw the trapezoid (Blue) formed by the split triangle
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(P2, v2);
        Gizmos.DrawLine(v2, v3);
        Gizmos.DrawLine(v3, P2);

        // Now split the trapezoid into two triangles
        Gizmos.color = Color.green;
        Gizmos.DrawLine(P2, v2);
        Gizmos.DrawLine(v2, v3);
        Gizmos.DrawLine(v3, P1); // New triangle from the trapezoid

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(v3, P1);
        Gizmos.DrawLine(P1, P2);
        Gizmos.DrawLine(P2, v3); // New triangle from the trapezoid
    }
}
