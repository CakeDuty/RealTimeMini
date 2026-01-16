using UnityEngine;

/// <summary>
/// Computes PCA on a Mesh's vertices (in world space) and draws the 3 principal axes.
/// Works entirely in Unity (no external tools).
/// </summary>
[ExecuteAlways]
public class MeshPCAVisualizer : MonoBehaviour
{
    public MeshFilter meshFilter;
    public SkinnedMeshRenderer skinned;   // optional if your Lucario is skinned/animated

    [Header("Draw")]
    public bool drawAxes = true;
    public float axisLength = 1.0f;
    public float axisThickness = 0.01f;

    // Results
    public Vector3 meanWorld;
    public Vector3 axis0World, axis1World, axis2World; // principal directions (unit)

    void OnEnable() => Recompute();
    void OnValidate() => Recompute();
    void Update()
    {
        // If skinned + animated, you can recompute every frame (costly). Otherwise keep it off.
        // Uncomment if needed:
        // if (skinned) Recompute();
    }

    [ContextMenu("Recompute PCA")]
    public void Recompute()
    {
        var verts = GetWorldVertices();
        if (verts == null || verts.Length < 3) return;

        // Mean
        Vector3 mean = Vector3.zero;
        for (int i = 0; i < verts.Length; i++) mean += verts[i];
        mean /= verts.Length;
        meanWorld = mean;

        // Covariance (3x3)
        double xx=0, xy=0, xz=0, yy=0, yz=0, zz=0;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 d = verts[i] - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z;
            zz += d.z * d.z;
        }
        double invN = 1.0 / Mathf.Max(1, verts.Length);
        xx *= invN; xy *= invN; xz *= invN; yy *= invN; yz *= invN; zz *= invN;

        // Power iteration to get top eigenvector, then deflate twice.
        Vector3 v0 = PowerIter(xx, xy, xz, yy, yz, zz, Vector3.right);
        double l0 = Rayleigh(xx, xy, xz, yy, yz, zz, v0);

        // Deflate: C1 = C - l0 * v0 v0^T
        Deflate(ref xx, ref xy, ref xz, ref yy, ref yz, ref zz, l0, v0);

        Vector3 v1 = PowerIter(xx, xy, xz, yy, yz, zz, Vector3.up);
        double l1 = Rayleigh(xx, xy, xz, yy, yz, zz, v1);
        Deflate(ref xx, ref xy, ref xz, ref yy, ref yz, ref zz, l1, v1);

        Vector3 v2 = Vector3.Cross(v0, v1).normalized; // third axis orthogonal

        axis0World = v0.normalized;
        axis1World = v1.normalized;
        axis2World = v2.normalized;
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

    static Vector3 MulCov(double xx,double xy,double xz,double yy,double yz,double zz, Vector3 v)
    {
        double x = xx*v.x + xy*v.y + xz*v.z;
        double y = xy*v.x + yy*v.y + yz*v.z;
        double z = xz*v.x + yz*v.y + zz*v.z;
        return new Vector3((float)x,(float)y,(float)z);
    }

    static Vector3 PowerIter(double xx,double xy,double xz,double yy,double yz,double zz, Vector3 seed)
    {
        Vector3 v = seed.normalized;
        for (int i = 0; i < 20; i++)
        {
            Vector3 w = MulCov(xx,xy,xz,yy,yz,zz, v);
            float mag = w.magnitude;
            if (mag < 1e-8f) break;
            v = w / mag;
        }
        return v;
    }

    static double Rayleigh(double xx,double xy,double xz,double yy,double yz,double zz, Vector3 v)
    {
        Vector3 w = MulCov(xx,xy,xz,yy,yz,zz, v);
        return Vector3.Dot(v, w);
    }

    static void Deflate(ref double xx, ref double xy, ref double xz, ref double yy, ref double yz, ref double zz, double lambda, Vector3 v)
    {
        // C -= lambda * v v^T
        double vx=v.x, vy=v.y, vz=v.z;
        xx -= lambda * vx*vx;
        xy -= lambda * vx*vy;
        xz -= lambda * vx*vz;
        yy -= lambda * vy*vy;
        yz -= lambda * vy*vz;
        zz -= lambda * vz*vz;
    }

    void OnDrawGizmos()
    {
        if (!drawAxes) return;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(meanWorld, axisThickness * 2f);

        DrawAxis(axis0World, Color.red);
        DrawAxis(axis1World, Color.green);
        DrawAxis(axis2World, Color.blue);
    }

    void DrawAxis(Vector3 dir, Color c)
    {
        Gizmos.color = c;
        Gizmos.DrawLine(meanWorld, meanWorld + dir * axisLength);
        Gizmos.DrawLine(meanWorld, meanWorld - dir * axisLength);
    }
}
