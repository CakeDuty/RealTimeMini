using UnityEngine; // Gives us access to Unity types like MonoBehaviour, Vector3, Gizmos, etc.

/// <summary>
/// Visualizes Principal Component Analysis (PCA) of a mesh:
/// - Reads all mesh vertices (in world space)
/// - Computes the mean position and a 3x3 covariance matrix
/// - Finds the three main directions (eigenvectors) of that covariance
/// - Draws those directions as 3 colored axes using Gizmos
/// This is all done inside Unity without external tools.
/// </summary>
[ExecuteAlways] // Make this script run in the Editor even when not in Play mode
public class MeshPCAVisualizer : MonoBehaviour
{
    // Reference to a MeshFilter if the mesh is a static mesh (no skinning/animation)
    public MeshFilter meshFilter;

    // Reference to a SkinnedMeshRenderer if the mesh is animated / skinned
    public SkinnedMeshRenderer skinned;

    [Header("Draw")] // Group the following fields under a "Draw" header in the Inspector
    public bool drawAxes = true;   // Toggle to enable/disable drawing of PCA axes
    public float axisLength = 1.0f;   // How long each axis line should be
    public float axisThickness = 0.01f; // Used only to scale the small center sphere

    // These fields store the computed PCA results so we can inspect and draw them.
    public Vector3 meanWorld;                       // Average (mean) position of all vertices in world space
    public Vector3 axis0World, axis1World, axis2World; // The three principal directions (unit vectors)

    // Called when the component is enabled in the scene
    void OnEnable() => Recompute(); // Immediately recompute PCA when the script becomes active

    // Called by Unity when a value is changed in the Inspector
    void OnValidate() => Recompute(); // Recompute PCA whenever we change parameters or references

    // Called every frame (in Edit mode as well because of [ExecuteAlways])
    void Update()
    {
        // If the mesh is skinned and animated, we *could* recompute each frame to follow the animation.
        // That is quite expensive, so we leave it commented out by default.
        // if (skinned) Recompute();
    }

    // Add a context menu entry so we can right-click the component and manually trigger recomputation
    [ContextMenu("Recompute PCA")]
    public void Recompute()
    {
        // Get all mesh vertices in world space (either from MeshFilter or SkinnedMeshRenderer)
        var verts = GetWorldVertices();

        // If we failed to get vertices or have fewer than 3 points, we cannot compute PCA, so we stop
        if (verts == null || verts.Length < 3) return;

        // ------------------ Compute the mean (average) position ------------------

        // Start the mean as the zero vector
        Vector3 mean = Vector3.zero;

        // Sum up all vertex positions
        for (int i = 0; i < verts.Length; i++)
            mean += verts[i];

        // Divide by the number of vertices to get the average
        mean /= verts.Length;

        // Store the result in the public field so we can see it and use it when drawing
        meanWorld = mean;

        // ------------------ Compute the 3x3 covariance matrix ------------------

        // We store each element of the symmetric covariance matrix:
        // [ xx  xy  xz ]
        // [ xy  yy  yz ]
        // [ xz  yz  zz ]
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;

        // Loop over all vertices and accumulate the covariance terms
        for (int i = 0; i < verts.Length; i++)
        {
            // d is the vector from the mean to the vertex
            Vector3 d = verts[i] - mean;

            // Add contributions to each covariance entry
            xx += d.x * d.x;  // variance in X
            xy += d.x * d.y;  // covariance X-Y
            xz += d.x * d.z;  // covariance X-Z
            yy += d.y * d.y;  // variance in Y
            yz += d.y * d.z;  // covariance Y-Z
            zz += d.z * d.z;  // variance in Z
        }

        // Compute 1/N where N is the number of vertices (use Mathf.Max to avoid division by zero)
        double invN = 1.0 / Mathf.Max(1, verts.Length);

        // Convert the accumulated sums into averages by multiplying with 1/N
        xx *= invN; xy *= invN; xz *= invN;
        yy *= invN; yz *= invN; zz *= invN;

        // ------------------ Find principal directions via eigenvectors ------------------

        // Use power iteration with an initial guess (Vector3.right) to get the dominant eigenvector
        Vector3 v0 = PowerIter(xx, xy, xz, yy, yz, zz, Vector3.right);

        // Compute the corresponding eigenvalue using the Rayleigh quotient
        double l0 = Rayleigh(xx, xy, xz, yy, yz, zz, v0);

        // Deflate the covariance matrix: remove the influence of the first eigenvector
        // This gives us a new covariance matrix with the first component “factored out”
        Deflate(ref xx, ref xy, ref xz, ref yy, ref yz, ref zz, l0, v0);

        // Run power iteration again on the deflated matrix to get the second eigenvector
        Vector3 v1 = PowerIter(xx, xy, xz, yy, yz, zz, Vector3.up);

        // Compute the second eigenvalue
        double l1 = Rayleigh(xx, xy, xz, yy, yz, zz, v1);

        // Deflate again to remove the second component if we wanted a third eigenvalue explicitly
        Deflate(ref xx, ref xy, ref xz, ref yy, ref yz, ref zz, l1, v1);

        // The third principal direction is orthogonal to the first two, so we can get it via cross product
        Vector3 v2 = Vector3.Cross(v0, v1).normalized;

        // Store normalized directions in the public fields for drawing
        axis0World = v0.normalized; // principal axis 0 (largest variance)
        axis1World = v1.normalized; // principal axis 1
        axis2World = v2.normalized; // principal axis 2 (orthogonal to the first two)
    }

    // Returns all vertex positions of the mesh in world space
    Vector3[] GetWorldVertices()
    {
        // If we have a skinned mesh renderer, we bake the current deformed mesh into a temporary Mesh
        if (skinned != null)
        {
            var baked = new Mesh();                  // Create an empty Mesh container
            skinned.BakeMesh(baked);                 // Fill it with the current skinned (animated) shape
            return TransformVerts(baked.vertices);   // Convert local vertices to world space and return them
        }

        // If no MeshFilter has been assigned, try to get one from the same GameObject
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();

        // If we still don't have a meshFilter or there is no mesh assigned, return null
        if (meshFilter == null || meshFilter.sharedMesh == null) return null;

        // Convert the MeshFilter’s local-space vertices to world space and return them
        return TransformVerts(meshFilter.sharedMesh.vertices);
    }

    // Transforms an array of local-space vertices into world space using this object's transform
    Vector3[] TransformVerts(Vector3[] localVerts)
    {
        // Create a new array to store world-space positions
        var world = new Vector3[localVerts.Length];

        // Get the local-to-world matrix for the GameObject this script is attached to
        var m = transform.localToWorldMatrix;

        // Multiply each local vertex by the matrix to move it into world space
        for (int i = 0; i < localVerts.Length; i++)
            world[i] = m.MultiplyPoint3x4(localVerts[i]);

        // Return the transformed vertices
        return world;
    }

    // Helper: multiply the covariance matrix by a vector v (C * v)
    static Vector3 MulCov(double xx, double xy, double xz,
                          double yy, double yz, double zz,
                          Vector3 v)
    {
        // Compute each component of the result by applying the covariance matrix
        double x = xx * v.x + xy * v.y + xz * v.z;
        double y = xy * v.x + yy * v.y + yz * v.z;
        double z = xz * v.x + yz * v.y + zz * v.z;

        // Convert back to Vector3 (cast double to float)
        return new Vector3((float)x, (float)y, (float)z);
    }

    // Power iteration: iteratively find the dominant eigenvector of the covariance matrix
    static Vector3 PowerIter(double xx, double xy, double xz,
                             double yy, double yz, double zz,
                             Vector3 seed)
    {
        // Start with a normalized version of the seed vector
        Vector3 v = seed.normalized;

        // Perform a fixed number of iterations to converge towards the dominant eigenvector
        for (int i = 0; i < 20; i++)
        {
            // Multiply covariance matrix by current vector estimate
            Vector3 w = MulCov(xx, xy, xz, yy, yz, zz, v);

            // Compute the length of the result
            float mag = w.magnitude;

            // If the result is extremely small, we break to avoid division by zero
            if (mag < 1e-8f) break;

            // Normalize w to get the next estimate of the eigenvector
            v = w / mag;
        }

        // Return the final (approximate) eigenvector
        return v;
    }

    // Rayleigh quotient: approximate eigenvalue associated with eigenvector v
    static double Rayleigh(double xx, double xy, double xz,
                           double yy, double yz, double zz,
                           Vector3 v)
    {
        // Compute w = C * v
        Vector3 w = MulCov(xx, xy, xz, yy, yz, zz, v);

        // Return v dot (C * v) which approximates the eigenvalue for v
        return Vector3.Dot(v, w);
    }

    // Deflation: modifies the covariance matrix to remove one eigencomponent
    static void Deflate(ref double xx, ref double xy, ref double xz,
                        ref double yy, ref double yz, ref double zz,
                        double lambda, Vector3 v)
    {
        // We subtract lambda * (v v^T) from the covariance matrix:
        // C_new = C_old - lambda * v v^T
        double vx = v.x, vy = v.y, vz = v.z;

        xx -= lambda * vx * vx;
        xy -= lambda * vx * vy;
        xz -= lambda * vx * vz;
        yy -= lambda * vy * vy;
        yz -= lambda * vy * vz;
        zz -= lambda * vz * vz;
    }

    // Unity callback to draw Gizmos in the Scene view
    void OnDrawGizmos()
    {
        // If drawing is disabled, exit early
        if (!drawAxes) return;

        // Draw a small sphere at the mean position so we see the origin of the axes
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(meanWorld, axisThickness * 2f);

        // Draw the three main axes in different colors (RGB)
        DrawAxis(axis0World, Color.red);   // First principal axis (largest variance)
        DrawAxis(axis1World, Color.green); // Second principal axis
        DrawAxis(axis2World, Color.blue);  // Third principal axis
    }

    // Draw a single axis as a line from the mean in both directions
    void DrawAxis(Vector3 dir, Color c)
    {
        // Set the Gizmo color for this axis
        Gizmos.color = c;

        // Draw a line from the mean in the positive direction
        Gizmos.DrawLine(meanWorld, meanWorld + dir * axisLength);

        // Draw a line from the mean in the negative direction
        Gizmos.DrawLine(meanWorld, meanWorld - dir * axisLength);
    }
}
