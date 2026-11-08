
using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public sealed class PoolManager : MonoBehaviour
{
    private const int RuntimeCreationsPerFrame = 2;
    private const int DestroysPerCheck = 2;

    public enum PoolPrefabSource
    {
        Inspector,
        ResourcesFolder
    }

    [Serializable]
    public sealed class PoolVariantConfig
    {
        public GameObject prefab;
        public int initialCount;
        public bool expandWhenEmpty = true;
    }

    [Serializable]
    public sealed class PoolGroupConfig
    {
        public PoolId id;
        public Transform parent;
        public PoolPrefabSource prefabSource =
            PoolPrefabSource.ResourcesFolder;

        [HideInInspector]
        public string resourcesFolder;

        public int resourceInitialCount;
        public bool resourceExpandWhenEmpty = true;
        public PoolVariantConfig[] variants;

        public bool enableAutoShrink;
        public float idleSecondsBeforeShrink = 60f;
        public float shrinkCheckIntervalSeconds = 10f;
    }

    private readonly struct AvailableEntry
    {
        public readonly PooledObject Pooled;
        public readonly float IdleSince;

        public AvailableEntry(PooledObject pooled, float idleSince)
        {
            Pooled = pooled;
            IdleSince = idleSince;
        }
    }

    private sealed class PoolBucket
    {
        public GameObject Prefab;
        public Transform Parent;
        public bool ExpandWhenEmpty;

        public int InitialCount;
        public int CreatedCount;

        public readonly Queue<AvailableEntry> Available =
            new Queue<AvailableEntry>();

        public readonly List<PooledObject> Active =
            new List<PooledObject>();
    }

    private sealed class PoolGroup
    {
        public PoolBucket[] Buckets;
        public int NextVariantIndex;

        public int CreationFrame = -1;
        public int CreationsThisFrame;

        public int CurrentActiveCount;
        public int PeakActiveCount;
        public int RuntimeCreationCount;
        public int FailedRentCount;
        public int DestroyedCount;
        public int TotalCreatedCount;

        public bool AutoShrink;
        public float IdleSecondsBeforeShrink;
        public float ShrinkCheckIntervalSeconds;
        public float NextShrinkTime;
        public int NextShrinkBucket;

        public readonly Dictionary<string, int> VariantByName =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private PoolGroupConfig[] groups;

    private readonly Dictionary<PoolId, PoolGroup> pools =
        new Dictionary<PoolId, PoolGroup>();

    private readonly List<PoolGroup> shrinkingGroups =
        new List<PoolGroup>();

    private Transform inactiveInstantiationRoot;
    private bool initialized;

    public bool AutoShrinkingEnabled { get; set; } = true;

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        if (!initialized || !AutoShrinkingEnabled)
            return;

        float now = Time.unscaledTime;

        for (int i = 0; i < shrinkingGroups.Count; i++)
        {
            PoolGroup group = shrinkingGroups[i];

            if (now < group.NextShrinkTime)
                continue;

            group.NextShrinkTime =
                now + group.ShrinkCheckIntervalSeconds;

            ShrinkGroup(group, now);
        }
    }

    private void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        if (groups == null)
            return;

        if (resourceManager == null)
            resourceManager = FindFirstObjectByType<ResourceManager>();

        for (int i = 0; i < groups.Length; i++)
        {
            PoolGroupConfig config = groups[i];

            if (config == null || pools.ContainsKey(config.id))
                continue;

            PoolVariantConfig[] variants = ResolveVariants(config);

            if (variants == null || variants.Length == 0)
                continue;

            PoolGroup group = new PoolGroup
            {
                Buckets = new PoolBucket[variants.Length],
                AutoShrink = config.enableAutoShrink,
                IdleSecondsBeforeShrink =
                    Mathf.Max(1f, config.idleSecondsBeforeShrink),
                ShrinkCheckIntervalSeconds =
                    Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds),
                NextShrinkTime = Time.unscaledTime +
                    Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds)
            };

            pools.Add(config.id, group);

            if (group.AutoShrink)
                shrinkingGroups.Add(group);

            for (int j = 0; j < variants.Length; j++)
            {
                PoolVariantConfig variant = variants[j];

                if (variant == null || variant.prefab == null)
                    continue;

                PoolBucket bucket = new PoolBucket
                {
                    Prefab = variant.prefab,
                    Parent = config.parent != null
                        ? config.parent
                        : transform,
                    InitialCount = Mathf.Max(0, variant.initialCount),
                    ExpandWhenEmpty = variant.expandWhenEmpty
                };

                group.Buckets[j] = bucket;

                if (!group.VariantByName.ContainsKey(bucket.Prefab.name))
                {
                    group.VariantByName.Add(
                        bucket.Prefab.name,
                        j
                    );
                }

                for (int k = 0; k < bucket.InitialCount; k++)
                    CreateInstance(config.id, j, group, bucket);
            }
        }
    }

    private PoolVariantConfig[] ResolveVariants(
        PoolGroupConfig config)
    {
        if (config.prefabSource == PoolPrefabSource.Inspector)
            return config.variants;

        if (resourceManager == null)
            return Array.Empty<PoolVariantConfig>();

        GameObject[] prefabs =
            resourceManager.LoadPrefabGroup(config.id);

        if (prefabs == null || prefabs.Length == 0)
            return Array.Empty<PoolVariantConfig>();

        PoolVariantConfig[] result =
            new PoolVariantConfig[prefabs.Length];

        int initialCount = Mathf.Max(
            0,
            config.resourceInitialCount
        );

        int countPerVariant = initialCount / prefabs.Length;
        int remainder = initialCount % prefabs.Length;

        for (int i = 0; i < prefabs.Length; i++)
        {
            result[i] = new PoolVariantConfig
            {
                prefab = prefabs[i],
                initialCount =
                    countPerVariant + (i < remainder ? 1 : 0),
                expandWhenEmpty = config.resourceExpandWhenEmpty
            };
        }

        return result;
    }

    private Transform GetInactiveInstantiationRoot()
    {
        if (inactiveInstantiationRoot != null)
            return inactiveInstantiationRoot;

        GameObject root = new GameObject("PoolInactiveStaging");
        root.SetActive(false);
        root.transform.SetParent(transform, false);

        inactiveInstantiationRoot = root.transform;

        return inactiveInstantiationRoot;
    }

    private PooledObject CreateInstance(
        PoolId id,
        int variantIndex,
        PoolGroup group,
        PoolBucket bucket)
    {
        GameObject instance = Instantiate(
            bucket.Prefab,
            GetInactiveInstantiationRoot(),
            false
        );

        instance.name = bucket.Prefab.name;
        instance.SetActive(false);

        if (!instance.TryGetComponent(out PooledObject pooled))
            pooled = instance.AddComponent<PooledObject>();

        pooled.Bind(this, id, variantIndex);

        instance.transform.SetParent(bucket.Parent, false);

        bucket.CreatedCount++;
        group.TotalCreatedCount++;

        bucket.Available.Enqueue(
            new AvailableEntry(pooled, Time.unscaledTime)
        );

        return pooled;
    }

    public GameObject Rent(PoolId id, int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, false, default, false, default
        );
    }

    public GameObject Rent(
        PoolId id,
        Vector3 position,
        int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, true, position, false, default
        );
    }

    public GameObject Rent(
        PoolId id,
        Vector3 position,
        Quaternion rotation,
        int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, true, position, true, rotation
        );
    }

    public GameObject Rent(PoolId id, string prefabName)
    {
        return TryGetVariantIndex(id, prefabName, out int index)
            ? Rent(id, index)
            : null;
    }

    public GameObject Rent(
        PoolId id,
        string prefabName,
        Vector3 position)
    {
        return TryGetVariantIndex(id, prefabName, out int index)
            ? Rent(id, position, index)
            : null;
    }

    public GameObject Rent(
        PoolId id,
        string prefabName,
        Vector3 position,
        Quaternion rotation)
    {
        return TryGetVariantIndex(id, prefabName, out int index)
            ? Rent(id, position, rotation, index)
            : null;
    }

    public bool TryGetVariantIndex(
        PoolId id,
        string prefabName,
        out int variantIndex)
    {
        Initialize();

        variantIndex = -1;

        return !string.IsNullOrEmpty(prefabName)
            && pools.TryGetValue(id, out PoolGroup group)
            && group.VariantByName.TryGetValue(
                prefabName,
                out variantIndex
            );
    }

    public int Prepare(
        PoolId id,
        string prefabName,
        int desiredAvailableCount)
    {
        if (!TryGetVariantIndex(id, prefabName, out int index))
            return 0;

        PoolGroup group = pools[id];
        PoolBucket bucket = group.Buckets[index];

        if (bucket == null || !bucket.ExpandWhenEmpty)
            return 0;

        int created = 0;

        while (bucket.Available.Count <
               Mathf.Max(0, desiredAvailableCount))
        {
            if (!CanCreateThisFrame(group))
                break;

            CreateInstance(id, index, group, bucket);

            group.CreationsThisFrame++;
            group.RuntimeCreationCount++;
            created++;
        }

        return created;
    }

    private static bool CanCreateThisFrame(PoolGroup group)
    {
        if (group.CreationFrame != Time.frameCount)
        {
            group.CreationFrame = Time.frameCount;
            group.CreationsThisFrame = 0;
        }

        return group.CreationsThisFrame <
            RuntimeCreationsPerFrame;
    }

    private GameObject RentInternal(
        PoolId id,
        int variantIndex,
        bool hasPosition,
        Vector3 position,
        bool hasRotation,
        Quaternion rotation)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group))
            return null;

        if (variantIndex >= 0)
        {
            if (variantIndex >= group.Buckets.Length)
                return null;

            GameObject result = RentFromBucket(
                id,
                variantIndex,
                group,
                group.Buckets[variantIndex],
                hasPosition,
                position,
                hasRotation,
                rotation
            );

            if (result == null)
                group.FailedRentCount++;

            return result;
        }

        for (int i = 0; i < group.Buckets.Length; i++)
        {
            int index =
                (group.NextVariantIndex + i) %
                group.Buckets.Length;

            GameObject result = RentFromBucket(
                id,
                index,
                group,
                group.Buckets[index],
                hasPosition,
                position,
                hasRotation,
                rotation
            );

            if (result == null)
                continue;

            group.NextVariantIndex =
                (index + 1) % group.Buckets.Length;

            return result;
        }

        group.FailedRentCount++;
        return null;
    }

    private GameObject RentFromBucket(
        PoolId id,
        int variantIndex,
        PoolGroup group,
        PoolBucket bucket,
        bool hasPosition,
        Vector3 position,
        bool hasRotation,
        Quaternion rotation)
    {
        if (bucket == null)
            return null;

        PooledObject pooled = null;

        while (bucket.Available.Count > 0)
        {
            pooled = bucket.Available.Dequeue().Pooled;

            if (pooled != null)
                break;

            bucket.CreatedCount =
                Mathf.Max(0, bucket.CreatedCount - 1);

            group.TotalCreatedCount =
                Mathf.Max(0, group.TotalCreatedCount - 1);
        }

        if (pooled == null &&
            bucket.ExpandWhenEmpty &&
            CanCreateThisFrame(group))
        {
            pooled = CreateInstance(
                id,
                variantIndex,
                group,
                bucket
            );

            bucket.Available.Dequeue();

            group.CreationsThisFrame++;
            group.RuntimeCreationCount++;
        }

        if (pooled == null)
            return null;

        Transform target = pooled.transform;

        if (hasPosition && hasRotation)
            target.SetPositionAndRotation(position, rotation);
        else if (hasPosition)
            target.position = position;
        else if (hasRotation)
            target.rotation = rotation;

        pooled.IsRented = true;
        pooled.ActiveIndex = bucket.Active.Count;

        bucket.Active.Add(pooled);

        group.CurrentActiveCount++;

        if (group.CurrentActiveCount > group.PeakActiveCount)
            group.PeakActiveCount = group.CurrentActiveCount;

        pooled.gameObject.SetActive(true);

        return pooled.gameObject;
    }

    public bool Return(GameObject instance)
    {
        if (instance == null ||
            !instance.TryGetComponent(out PooledObject pooled))
            return false;

        if (pooled.Owner != this || !pooled.IsRented)
            return false;

        if (!pools.TryGetValue(
            pooled.PoolId,
            out PoolGroup group))
            return false;

        int variantIndex = pooled.VariantIndex;

        if (variantIndex < 0 ||
            variantIndex >= group.Buckets.Length)
            return false;

        PoolBucket bucket = group.Buckets[variantIndex];

        if (bucket == null)
            return false;

        int index = pooled.ActiveIndex;
        int lastIndex = bucket.Active.Count - 1;

        if (index < 0 ||
            index > lastIndex ||
            bucket.Active[index] != pooled)
            return false;

        PooledObject last = bucket.Active[lastIndex];

        bucket.Active[index] = last;
        last.ActiveIndex = index;

        bucket.Active.RemoveAt(lastIndex);
        group.CurrentActiveCount--;

        pooled.IsRented = false;
        pooled.ActiveIndex = -1;

        instance.SetActive(false);

        if (instance.transform.parent != bucket.Parent)
            instance.transform.SetParent(bucket.Parent, false);

        bucket.Available.Enqueue(
            new AvailableEntry(pooled, Time.unscaledTime)
        );

        return true;
    }

    public void ReturnAll(PoolId id)
    {
        if (!pools.TryGetValue(id, out PoolGroup group))
            return;

        for (int i = 0; i < group.Buckets.Length; i++)
        {
            PoolBucket bucket = group.Buckets[i];

            if (bucket == null)
                continue;

            while (bucket.Active.Count > 0)
            {
                PooledObject pooled =
                    bucket.Active[bucket.Active.Count - 1];

                if (pooled != null && Return(pooled.gameObject))
                    continue;

                bucket.Active.RemoveAt(
                    bucket.Active.Count - 1
                );

                bucket.CreatedCount =
                    Mathf.Max(0, bucket.CreatedCount - 1);

                group.TotalCreatedCount =
                    Mathf.Max(0, group.TotalCreatedCount - 1);

                group.CurrentActiveCount =
                    Mathf.Max(0, group.CurrentActiveCount - 1);
            }
        }
    }

    private void ShrinkGroup(PoolGroup group, float now)
    {
        int destroyed = 0;
        int inspected = 0;

        while (destroyed < DestroysPerCheck &&
               inspected < group.Buckets.Length)
        {
            int index = group.NextShrinkBucket;

            group.NextShrinkBucket =
                (index + 1) % group.Buckets.Length;

            inspected++;

            PoolBucket bucket = group.Buckets[index];

            if (bucket == null)
                continue;

            while (bucket.Available.Count > 0 &&
                   bucket.Available.Peek().Pooled == null)
            {
                bucket.Available.Dequeue();

                bucket.CreatedCount =
                    Mathf.Max(0, bucket.CreatedCount - 1);

                group.TotalCreatedCount =
                    Mathf.Max(0, group.TotalCreatedCount - 1);
            }

            if (bucket.CreatedCount <= bucket.InitialCount ||
                bucket.Available.Count == 0)
                continue;

            AvailableEntry oldest = bucket.Available.Peek();

            if (now - oldest.IdleSince <
                group.IdleSecondsBeforeShrink)
                continue;

            bucket.Available.Dequeue();
            bucket.CreatedCount--;
            group.TotalCreatedCount--;
            group.DestroyedCount++;

            Destroy(oldest.Pooled.gameObject);
            destroyed++;
        }
    }

    public int GetVariantCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.Buckets.Length
            : 0;
    }

    public int GetActiveCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.CurrentActiveCount
            : 0;
    }

    public int GetCreatedCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.TotalCreatedCount
            : 0;
    }

    public int GetCreatedCount(PoolId id, int variantIndex)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group) ||
            variantIndex < 0 ||
            variantIndex >= group.Buckets.Length)
            return 0;

        PoolBucket bucket = group.Buckets[variantIndex];

        return bucket != null ? bucket.CreatedCount : 0;
    }

    public int GetAvailableCount(PoolId id)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group))
            return 0;

        int total = 0;

        for (int i = 0; i < group.Buckets.Length; i++)
        {
            if (group.Buckets[i] != null)
                total += group.Buckets[i].Available.Count;
        }

        return total;
    }

    public int GetPeakActiveCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.PeakActiveCount
            : 0;
    }

    public int GetRuntimeCreationCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.RuntimeCreationCount
            : 0;
    }

    public int GetFailedRentCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.FailedRentCount
            : 0;
    }

    public int GetDestroyedCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.DestroyedCount
            : 0;
    }
}
