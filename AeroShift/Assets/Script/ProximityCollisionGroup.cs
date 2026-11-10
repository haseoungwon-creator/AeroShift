using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProximityCollisionGroup : MonoBehaviour
{
    private Collider[] colliders;
    private bool[] originalEnabledStates;
    private bool[] tracked;
    private int configuredLayerMask;
    private bool initialized;
    private bool collisionsEnabled = true;
    private Bounds localBounds;
    private bool boundsValid;

    public int TrackedColliderCount { get; private set; }
    public bool CollisionsEnabled => collisionsEnabled;

    public void Initialize(LayerMask obstacleLayers)
    {
        int layerMask = obstacleLayers.value;

        if (initialized && configuredLayerMask == layerMask)
            return;

        if (initialized)
            SetCollisionsEnabled(true);
        else
        {
            // The current map prefabs have fixed child geometry. Cache once per instance.
            colliders = GetComponentsInChildren<Collider>(true);
            originalEnabledStates = new bool[colliders.Length];
            tracked = new bool[colliders.Length];

            for (int i = 0; i < colliders.Length; i++)
                originalEnabledStates[i] = colliders[i] != null && colliders[i].enabled;
        }

        TrackedColliderCount = 0;
        bool hasBounds = false;
        boundsValid = true;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider current = colliders[i];
            tracked[i] = current != null && ((1 << current.gameObject.layer) & layerMask) != 0;

            if (!tracked[i])
                continue;

            TrackedColliderCount++;

            if (!TryGetLocalBounds(transform, current, out Bounds bounds))
            {
                boundsValid = false;
                continue;
            }

            if (hasBounds)
                localBounds.Encapsulate(bounds);
            else
                localBounds = bounds;

            hasBounds = true;
        }

        boundsValid &= hasBounds;
        configuredLayerMask = layerMask;
        initialized = true;
        collisionsEnabled = true;
    }

    public bool SetCollisionsEnabled(bool enable)
    {
        if (!initialized || collisionsEnabled == enable)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider current = colliders[i];

            if (!tracked[i] || current == null)
                continue;

            bool desired = enable && originalEnabledStates[i];

            if (current.enabled != desired)
                current.enabled = desired;
        }

        collisionsEnabled = enable;
        return true;
    }

    public bool TryGetWorldZRange(out float minZ, out float maxZ)
    {
        minZ = maxZ = 0f;

        if (!boundsValid)
            return false;

        GetWorldZRange(transform, localBounds, out minZ, out maxZ);
        return true;
    }

    // Collider.bounds is empty while inactive. Use cached geometry instead.
    // Unknown shapes fail open: MapManager keeps their collisions enabled.
    internal static bool TryGetLocalBounds(Transform root, Collider collider, out Bounds bounds)
    {
        Bounds shape;

        if (collider is BoxCollider box)
            shape = new Bounds(box.center, box.size);
        else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
            shape = mesh.sharedMesh.bounds;
        else
        {
            bounds = default;
            return false;
        }

        Matrix4x4 matrix = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
        Vector3 extents = shape.extents;
        Vector3 transformedExtents = new Vector3(
            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
        bounds = new Bounds(matrix.MultiplyPoint3x4(shape.center), transformedExtents * 2f);
        return true;
    }

    internal static void GetWorldZRange(Transform root, Bounds bounds, out float minZ, out float maxZ)
    {
        Matrix4x4 matrix = root.localToWorldMatrix;
        float centerZ = matrix.MultiplyPoint3x4(bounds.center).z;
        Vector3 extents = bounds.extents;
        float extentZ = Mathf.Abs(matrix.m20) * extents.x +
            Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z;
        minZ = centerZ - extentZ;
        maxZ = centerZ + extentZ;
    }

    private void OnDisable()
    {
        SetCollisionsEnabled(true);
    }
}
