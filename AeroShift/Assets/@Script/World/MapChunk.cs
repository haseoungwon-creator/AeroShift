using UnityEngine;

public sealed class MapChunk : MonoBehaviour
{
    public bool IsSafe { get; private set; }
    public ProximityCollisionGroup CollisionGroup { get; private set; }

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

    public void SetChunkData(bool isSafe)
    {
        IsSafe = isSafe;
    }
}
