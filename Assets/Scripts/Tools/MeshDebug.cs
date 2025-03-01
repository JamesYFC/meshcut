using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class MeshDebug : MonoBehaviour
{
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        var mesh = meshFilter.mesh;
        Debug.Log($"vertices ({mesh.vertices.Length}): {string.Join(", ", mesh.vertices)}");
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var tris = mesh.GetTriangles(i).GroupByTripletsStrict();
            Debug.Log($"tris - submesh {i} ({tris.Count()}): {string.Join(", ", tris)}");
        }
        Debug.Log($"UVs (channel 0): ({mesh.uv.Length}): {string.Join(", ", mesh.uv)}");

        Debug.Log($"normals ({mesh.normals.Length}): {string.Join(", ", mesh.normals)}");
    }

    void OnDrawGizmos()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        Gizmos.color = Color.green; // Color for normals
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(vertices[i]);
            Vector3 worldNormal = transform.TransformDirection(normals[i]);
            Gizmos.DrawLine(worldPos, worldPos + worldNormal * 0.2f);
        }

        foreach (var n in normals)
        {
            if (n == Vector3.zero)
                Debug.Log("zeroN");
        }

        // are any pos/normal pairs duplicates?
        var hs = new HashSet<(Vector3, Vector3)>();
        var x = vertices
            .Zip(
                normals,
                (x, y) =>
                {
                    if (!hs.Add((x, y)))
                        Debug.Log($"{(x, y)} is dupe");

                    return (x, y);
                }
            )
            .ToList();
    }
}
