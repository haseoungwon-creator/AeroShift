using UnityEngine;

[DisallowMultipleComponent]
public sealed class PooledObject : MonoBehaviour
{
    public PoolId PoolId { get; private set; }
    public int VariantIndex { get; private set; }
    public bool IsRented { get; internal set; }
    internal int ActiveIndex { get; set; } = -1;
    internal PoolManager Owner { get; private set; }

    internal void Bind(PoolManager owner, PoolId poolId, int variantIndex)
    {
        Owner = owner;
        PoolId = poolId;
        VariantIndex = variantIndex;
        ActiveIndex = -1;
        IsRented = false;
    }

    public void Release()
    {
        if (Owner != null)
            Owner.Return(gameObject);
    }
}
