using System.Linq;
using UnityEditor;
using UnityEngine;

public class TriSortingTest : MonoBehaviour
{
    public Vector3 v1 = new Vector3(1, 1, 0);
    public Vector3 v2 = new Vector3(2, 2, 1);
    public Vector3 v3 = new Vector3(0, 3, 2);

    void OnDrawGizmos()
    {
        // Sort using dominant-axis projection (may fail)
        Vector3[] sortedByAxis = SortByDominantAxis(v1, v2, v3);
        DrawTriangle(sortedByAxis, Color.red);

        Vector3 offset = Vector3.right * 1f;

        // Sort using true plane projection (correct)
        Vector3[] sortedByPlane = SortByPlaneProjection(v1, v2, v3);
        DrawTriangle(sortedByPlane.Select(v => v + offset).ToArray(), Color.green);
    }

    Vector3[] SortByDominantAxis(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1);
        int axis =
            Mathf.Abs(normal.z) >= Mathf.Abs(normal.x) && Mathf.Abs(normal.z) >= Mathf.Abs(normal.y)
                ? 2
                : Mathf.Abs(normal.y) >= Mathf.Abs(normal.x)
                    ? 1
                    : 0;

        Vector3 center = (v1 + v2 + v3) / 3f;
        Vector3[] vertices = { v1, v2, v3 };

        return vertices
            .Select(v =>
            {
                float angle =
                    axis == 2
                        ? Mathf.Atan2(v.y - center.y, v.x - center.x)
                        : axis == 1
                            ? Mathf.Atan2(v.z - center.z, v.x - center.x)
                            : Mathf.Atan2(v.z - center.z, v.y - center.y);
                return (v, angle);
            })
            .OrderByDescending(p => p.angle)
            .Select(p => p.v)
            .ToArray();
    }

    Vector3[] SortByPlaneProjection(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
        Vector3 right = Vector3.Cross(normal, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(normal, Vector3.forward);
        right.Normalize();
        Vector3 up = Vector3.Cross(normal, right);

        Vector3 center = (v1 + v2 + v3) / 3f;
        Vector2 To2D(Vector3 v) =>
            new Vector2(Vector3.Dot(v - center, right), Vector3.Dot(v - center, up));

        return new[] { v1, v2, v3 }
            .Select(v => (v, angle: Mathf.Atan2(To2D(v).y, To2D(v).x)))
            .OrderByDescending(p => p.angle)
            .Select(p => p.v)
            .ToArray();
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
