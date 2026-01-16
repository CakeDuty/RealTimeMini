using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Approximate voxelization inside mesh bounds by sampling a 3D grid
/// and marking cells occupied based on distance to nearest vertex (cheap, Unity-only).
/// Renders cubes using Gizmos (or you can instantiate cubes if needed).
/// </summary>
[ExecuteAlways]
public class MeshVoxelVisualizer : MonoBehaviour
{
    public MeshFilter meshFilter;
    public SkinnedMeshRenderer skinned;

    [Header("Grid")]
    [Range(8, 64)] public int resolution = 24;
    public float occupancyDistance = 0.05f; // tune per model scale
    public bool drawVoxels = true;

    [Header("Animation")]
    public bool explode = false;
    public float explodeAmount = 0.5f;
    public float explodeSpeed = 1.0f;

    Vector3[] vertsWorld;
    Bounds boundsWorld;
    List<Vector3> occupiedCenters = new List<Vector3>();

    void OnEnable() => Rebuild();
    void OnValidate() => Rebuild();

    [ContextMenu("Rebuild Voxels")]
    public void Rebuild()
    {
        vertsWorld = GetWorldVertices();
        if (vertsWorld == null || vertsWorld.Length == 0) return;

        // world bounds
        boundsWorld = new Bounds(vertsWorld[0], Vector3.zero);
        for (int i = 1; i < vertsWorld.Length; i++) boundsWorld.Encapsulate(vertsWorld[i]);

        occupiedCenters.Clear();

        Vector3 min = boundsWorld.min;
        Vector3 size = boundsWorld.size;
        float step = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / resolution;

        // Sample grid
        for (int xi = 0; xi < resolution; xi++)
        for (int yi = 0; yi < resolution; yi++)
        for (int zi = 0; zi < resolution; zi++)
        {
            Vector3 p = min + new Vector3(xi + 0.5f, yi + 0.5f, zi + 0.5f) * step;

            // distance-to-vertices approximation (O(N) per cell; OK at low resolution)
            float best = float.PositiveInfinity;
            for (int v = 0; v < vertsWorld.Length; v++)
            {
                float d = (vertsWorld[v] - p).sqrMagnitude;
                if (d < best) best = d;
            }

            if (best <= occupancyDistance * occupancyDistance)
                occupiedCenters.Add(p);
        }
    }

    Vector3[] GetWorldVertices()
    {
        if (skinned != null)
        {
            var baked = new Mesh();
            skinned.BakeMesh(baked);
            return TransformVerts(baked.vertices);
        }

        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return null;
        return TransformVerts(meshFilter.sharedMesh.vertices);
    }

    Vector3[] TransformVerts(Vector3[] localVerts)
    {
        var world = new Vector3[localVerts.Length];
        var m = transform.localToWorldMatrix;
        for (int i = 0; i < localVerts.Length; i++)
            world[i] = m.MultiplyPoint3x4(localVerts[i]);
        return world;
    }

    void OnDrawGizmos()
    {
        if (!drawVoxels || occupiedCenters == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.8f);

        // Draw bounds
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(boundsWorld.center, boundsWorld.size);

        float t = explode ? (Mathf.Sin(Time.time * explodeSpeed) * 0.5f + 0.5f) : 0f;

        // voxel size estimate
        float step = Mathf.Max(boundsWorld.size.x, Mathf.Max(boundsWorld.size.y, boundsWorld.size.z)) / resolution;
        float cube = step * 0.9f;

        for (int i = 0; i < occupiedCenters.Count; i++)
        {
            Vector3 p = occupiedCenters[i];
            if (explode)
            {
                Vector3 dir = (p - boundsWorld.center).normalized;
                p += dir * explodeAmount * t;
            }

            Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.8f);
            Gizmos.DrawCube(p, Vector3.one * cube);
        }
    }
}
