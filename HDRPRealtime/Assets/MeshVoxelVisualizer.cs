using System.Collections.Generic; // Gives us access to List<T> and other collection types
using UnityEngine;                // Gives us Unity types like MonoBehaviour, Mesh, Gizmos, etc.

/// <summary>
/// Approximate voxelization inside mesh bounds by:
///  - sampling a 3D grid inside the mesh's bounding box,
///  - marking grid cells as occupied if they are close to any mesh vertex,
///  - and drawing those occupied cells as cubes using Gizmos.
/// This is a cheap approximation done entirely in Unity (no external libraries).
/// </summary>
[ExecuteAlways] // Run this script in Edit Mode as well as Play Mode
public class MeshVoxelVisualizer : MonoBehaviour
{
    // Reference to a MeshFilter for static meshes (non‑skinned).
    // If the Lucario model is a normal mesh, this is what we use.
    public MeshFilter meshFilter;

    // Reference to a SkinnedMeshRenderer for skinned / animated meshes.
    // If the model has bones and animations, we can bake that into a Mesh.
    public SkinnedMeshRenderer skinned;

    [Header("Grid")] // Group these settings under "Grid" in the Inspector
    [Range(8, 64)] public int resolution = 24; // Number of cells along each axis of the 3D grid
    public float occupancyDistance = 0.05f;    // How close a cell center must be to a vertex to count as "occupied"
    public bool drawVoxels = true;             // Master toggle to draw or hide the voxels

    [Header("Animation")] // Group animation options
    public bool explode = false;       // If true, voxels will pulse outward and inward
    public float explodeAmount = 0.5f; // How far voxels move away from the center at maximum
    public float explodeSpeed = 1.0f;  // How fast the explode / implode cycle runs

    // Cached world‑space vertices of the mesh used for voxelization
    Vector3[] vertsWorld;

    // World‑space axis‑aligned bounding box of the mesh
    Bounds boundsWorld;

    // List of voxel cell centers that are marked as occupied
    List<Vector3> occupiedCenters = new List<Vector3>();

    // Called when the component is enabled
    void OnEnable() => Rebuild(); // Immediately build the voxel representation when the script turns on

    // Called when a value in the Inspector is changed
    void OnValidate() => Rebuild(); // Rebuild voxels whenever settings or references change

    // Adds a context menu item so we can manually trigger rebuilding from the Inspector
    [ContextMenu("Rebuild Voxels")]
    public void Rebuild()
    {
        // Get all mesh vertices in world space
        vertsWorld = GetWorldVertices();

        // If we could not get vertices, or the mesh is empty, stop here
        if (vertsWorld == null || vertsWorld.Length == 0) return;

        // -------------- Compute world‑space bounds of the mesh --------------

        // Start bounds at the first vertex with zero size
        boundsWorld = new Bounds(vertsWorld[0], Vector3.zero);

        // Expand bounds so that it includes every vertex
        for (int i = 1; i < vertsWorld.Length; i++)
            boundsWorld.Encapsulate(vertsWorld[i]);

        // Clear any previously stored occupied voxel centers
        occupiedCenters.Clear();

        // Cache the minimum corner and size of the bounds for grid calculations
        Vector3 min = boundsWorld.min;   // minimum x,y,z of the bounds
        Vector3 size = boundsWorld.size; // total width/height/depth of the bounds

        // Compute a step size so that the grid fits in the largest dimension of the bounds
        // (we use the max dimension so voxels are roughly cubic even for non‑cube bounds)
        float step = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / resolution;

        // -------------- Sample the 3D grid and mark occupied cells --------------

        // Triple nested loop: iterate over all grid cells along x, y, and z
        for (int xi = 0; xi < resolution; xi++)
        for (int yi = 0; yi < resolution; yi++)
        for (int zi = 0; zi < resolution; zi++)
        {
            // Compute the center position of the current grid cell in world space.
            // We add 0.5 to go from cell index (corner) to cell center, then multiply by step.
            Vector3 p = min + new Vector3(xi + 0.5f, yi + 0.5f, zi + 0.5f) * step;

            // -------------- Approximate occupancy test --------------
            // We check the squared distance from this cell center to the nearest mesh vertex.
            // If it is within occupancyDistance, we treat the cell as "inside / on" the mesh.

            float best = float.PositiveInfinity; // Start with an extremely large distance

            // Loop over all vertices and track the smallest squared distance
            for (int v = 0; v < vertsWorld.Length; v++)
            {
                // Vector from cell center to vertex
                float d = (vertsWorld[v] - p).sqrMagnitude;

                // If this vertex is closer, update best distance
                if (d < best) best = d;
            }

            // If the best squared distance is smaller than the squared threshold,
            // we consider this grid cell occupied by the mesh.
            if (best <= occupancyDistance * occupancyDistance)
                occupiedCenters.Add(p); // Store the cell center so we can draw it later
        }
    }

    // Returns all mesh vertices in world space, either from SkinnedMeshRenderer or MeshFilter
    Vector3[] GetWorldVertices()
    {
        // If we have a skinned mesh renderer, we bake its current deformed mesh into a temporary Mesh
        if (skinned != null)
        {
            var baked = new Mesh();          // Create an empty Mesh container
            skinned.BakeMesh(baked);         // Fill it with the current skinned (animated) mesh
            return TransformVerts(baked.vertices); // Transform baked vertices to world space
        }

        // If no MeshFilter has been manually assigned, try to get one from this GameObject
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();

        // If we still don't have a mesh, or the MeshFilter has no mesh, return null
        if (meshFilter == null || meshFilter.sharedMesh == null) return null;

        // Transform the MeshFilter’s local‑space vertices into world space and return them
        return TransformVerts(meshFilter.sharedMesh.vertices);
    }

    // Converts an array of local‑space vertices to world‑space using this object's transform
    Vector3[] TransformVerts(Vector3[] localVerts)
    {
        // Create an array to hold the transformed positions
        var world = new Vector3[localVerts.Length];

        // Get the local‑to‑world transformation matrix of this GameObject
        var m = transform.localToWorldMatrix;

        // Multiply each local vertex position by the matrix to move it into world space
        for (int i = 0; i < localVerts.Length; i++)
            world[i] = m.MultiplyPoint3x4(localVerts[i]);

        // Return the transformed vertices
        return world;
    }

    // Called by Unity to draw Gizmos in the Scene view
    void OnDrawGizmos()
    {
        // If drawing is disabled or we have no occupied cells, do nothing
        if (!drawVoxels || occupiedCenters == null) return;

        // Set initial Gizmo color (we will override it again below, but this is a default)
        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.8f);

        // -------------- Draw the bounding box of the mesh --------------

        Gizmos.color = Color.white; // White for the bounding box outline
        Gizmos.DrawWireCube(boundsWorld.center, boundsWorld.size); // Draw the bounds as a wire cube

        // Compute an animation parameter t in [0,1] if explode is active.
        // We use a sine wave to make voxels smoothly move out and back in.
        float t = explode
            ? (Mathf.Sin(Time.time * explodeSpeed) * 0.5f + 0.5f) // Map sine from [-1,1] to [0,1]
            : 0f; // If explode is off, t stays 0 and voxels do not move

        // -------------- Compute voxel cube size for drawing --------------

        // Recompute the step size from the bounds in case they changed
        float step = Mathf.Max(boundsWorld.size.x,
                               Mathf.Max(boundsWorld.size.y, boundsWorld.size.z)) / resolution;

        // Slightly shrink cubes (0.9) so they do not touch each other perfectly and look cleaner
        float cube = step * 0.9f;

        // Loop over all occupied voxel centers and draw a cube at each position
        for (int i = 0; i < occupiedCenters.Count; i++)
        {
            // Start with the stored center position
            Vector3 p = occupiedCenters[i];

            // If explode animation is enabled, offset the cube along the direction
            // from the center of the bounds to the voxel center.
            if (explode)
            {
                // Direction from global center to this voxel
                Vector3 dir = (p - boundsWorld.center).normalized;

                // Move p outward by explodeAmount scaled by the animation parameter t
                p += dir * explodeAmount * t;
            }

            // Set the color for voxels (semi‑transparent cyan)
            Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.8f);

            // Draw the cube at position p with the computed cube size
            Gizmos.DrawCube(p, Vector3.one * cube);
        }
    }
}
