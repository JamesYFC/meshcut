using UnityEngine;

[ExecuteAlways]
public class VisualiseBoxCast : MonoBehaviour
{
    public float maxDistance = 1f;

    void Update()
    {
        if (
            Physics.BoxCast(
                transform.position,
                transform.lossyScale * 0.5f,
                transform.forward,
                Quaternion.identity,
                maxDistance
            )
        )
        {
            Debug.Log("The BoxCast hit something!");
        }
    }

    private void OnDrawGizmos()
    {
        DrawBoxCast(
            transform.position,
            transform.position + maxDistance * transform.forward,
            transform.lossyScale,
            transform.rotation
        );
    }

    void DrawBoxCast(Vector3 start, Vector3 end, Vector3 size, Quaternion rotation)
    {
        Gizmos.color = Color.green;

        // Cache the Gizmos matrix.
        Matrix4x4 currentMatrix = Gizmos.matrix;

        // Draw Cubes
        Gizmos.matrix = Matrix4x4.TRS(start, rotation, size);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.TRS(end, rotation, size);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        // Draw Connecting Lines
        Vector3 x = Vector3.right * size.x * 0.5f;
        Vector3 y = Vector3.up * size.y * 0.5f;
        Vector3 z = Vector3.forward * size.z * 0.5f;
        Gizmos.matrix = Matrix4x4.TRS(start, rotation, Vector3.one);
        Gizmos.DrawRay(Vector3.zero - x - y - z, Vector3.forward * maxDistance);
        Gizmos.DrawRay(Vector3.zero - x + y - z, Vector3.forward * maxDistance);
        Gizmos.DrawRay(Vector3.zero + x - y - z, Vector3.forward * maxDistance);
        Gizmos.DrawRay(Vector3.zero + x + y - z, Vector3.forward * maxDistance);

        // Reset the Gizmos matrix.
        Gizmos.matrix = currentMatrix;
    }
}
