using System.Linq;
using UnityEditor;
using UnityEngine;

public class TriCrossTest : MonoBehaviour
{
    public Vector3 v1 = new Vector3(1, 1, 0);
    public Vector3 v2 = new Vector3(2, 2, 1);
    public Vector3 v3 = new Vector3(0, 3, 2);

    void OnDrawGizmos()
    {
        DrawTriangle(new[] { v1, v2, v3 }, Color.green);
    }

    void DrawTriangle(Vector3[] verts, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(verts[0], verts[1]);
        Gizmos.DrawLine(verts[1], verts[2]);
        Gizmos.DrawLine(verts[2], verts[0]);

        // Label vertices with numbers
        Handles.color = color;
        Handles.Label(verts[0], "1");
        Handles.Label(verts[1], "2");
        Handles.Label(verts[2], "3");
    }
}
