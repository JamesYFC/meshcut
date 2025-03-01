using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        Vector2[] uvs = mesh.uv;
        Vector2[] uv2s = mesh.uv2;

        Gizmos.color = Color.green; // Color for normals
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(vertices[i]);
            Vector3 worldNormal = transform.TransformDirection(normals[i]);
            Gizmos.DrawLine(worldPos, worldPos + worldNormal * 0.2f);
        }

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i];
            if (n == Vector3.zero)
                Debug.Log("zeroN at " + i);
        }
        Debug.Log("ALL:");
        Debug.Log(
            string.Join(
                ", ",
                vertices.Select((v, i) => i + ": " + (vertices[i], uvs[i], normals[i]) + "\n")
            )
        );
        // are any vdata dupes?
        var hs = new Dictionary<(Vector3, Vector2, Vector3), (int index, int count)>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = (vertices[i], uvs[i], normals[i]);
            if (!hs.TryAdd(v, (i, 1)))
            {
                var (index, count) = hs[v];
                Debug.Log($"{v} occurred at {i}, but is also occurring at {index}");
                hs[v] = (index, count + 1);
            }
        }

        var dupes = hs.Where(kvp => kvp.Value.count > 1).ToList();
        Debug.Log($"dupes({dupes.Count}): {string.Join(", ", dupes)}");
        mesh.Optimize();
        Debug.Log($"new dupes({dupes.Count}): {string.Join(", ", dupes)}");
    }
}
