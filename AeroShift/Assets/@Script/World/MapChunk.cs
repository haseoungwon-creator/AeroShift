using System.Collections.Generic;
using UnityEngine;

public sealed class MapChunk : MonoBehaviour
{
    public bool IsSafe { get; private set; }
    public ProximityCollisionGroup CollisionGroup { get; private set; }

    private PoolManager structurePool;
    private readonly List<GameObject> rentedStructures = new List<GameObject>(32);

    public void SetChunkData(bool isSafe)
    {
        IsSafe = isSafe;
    }

    // This is called while the chunk is inactive. No new rock instances are created here.
    public bool ApplyLayout(MapLayoutDefinition layout, int[] variants, PoolManager pool)
    {
        ReleaseStructures();
        if (layout == null || layout.structures == null || variants == null ||
            layout.structures.Length != variants.Length || pool == null)
            return false;

        structurePool = pool;

        for (int i = 0; i < layout.structures.Length; i++)
        {
            MapStructurePlacement placement = layout.structures[i];
            Vector3 worldPosition = transform.TransformPoint(placement.position);
            Quaternion worldRotation = transform.rotation * Quaternion.Euler(placement.rotation);

            GameObject structure = pool.RentInactive(
                PoolId.MapStructure, worldPosition, worldRotation, transform, variants[i]);

            if (structure == null)
            {
                ReleaseStructures();
                return false;
            }

            structure.transform.localScale = placement.scale;
            rentedStructures.Add(structure);
            structure.SetActive(true);
        }

        // Colliders were added dynamically, so the old cached collider array is stale.
        // Rebuild it once per layout, not every FixedUpdate.
        if (CollisionGroup != null)
            CollisionGroup.Rebuild();

        return true;
    }

    public void ReleaseStructures()
    {
        if (CollisionGroup != null)
            CollisionGroup.Invalidate();

        if (structurePool != null)
        {
            for (int i = rentedStructures.Count - 1; i >= 0; i--)
            {
                GameObject structure = rentedStructures[i];
                if (structure != null)
                    structurePool.Return(structure);
            }
        }

        rentedStructures.Clear();
        structurePool = null;
    }

    public ProximityCollisionGroup GetCollisionGroup(LayerMask obstacleLayers)
    {
        if (CollisionGroup == null)
        {
            if (!TryGetComponent(out ProximityCollisionGroup group))
                group = gameObject.AddComponent<ProximityCollisionGroup>();
            CollisionGroup = group;
        }

        CollisionGroup.Initialize(obstacleLayers);
        return CollisionGroup;
    }

    // Do not return or reparent child structures from OnDisable/OnDestroy.
    // Unity forbids hierarchy changes while an ancestor is being activated
    // or deactivated. PoolManager.Return() releases structures before the
    // chunk is disabled, and ApplyLayout() also clears any stale layout.
}
