using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

public sealed class PoolManager : MonoBehaviour
{
    private static readonly ProfilerMarker CreateInstanceMarker = new ProfilerMarker("AeroShift.Pool.CreateInstance");

    public enum PoolPrefabSource
    {
        Inspector,
        ResourcesFolder
    }

    public sealed class PoolVariantConfig
    {
        public GameObject prefab;
        public int initialCount = 0;
        public int maxCount = 10;
        public bool expandWhenEmpty;
        public int minRetainedCount;
    }

    public sealed class PoolGroupConfig
    {
        public PoolId id;
        public Transform parent;
        public PoolPrefabSource prefabSource = PoolPrefabSource.ResourcesFolder;
        public string resourcesFolder;
        public int resourceInitialCount;
        public int resourceMaxCount = 32;
        public bool resourceExpandWhenEmpty = true;
        public PoolVariantConfig[] variants;
        public int maxRuntimeCreationsPerFrame;
        public bool enableAutoShrink;
        public int resourceMinRetainedCount;
        public float idleSecondsBeforeShrink = 60f;
        public float shrinkCheckIntervalSeconds = 10f;
        public int maxDestroysPerCheck = 2;
    }

    [SerializeField] ResourceManager resourceManager;
    [SerializeField] PoolGroupConfig[] groups;

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
        public int MaxCount;
        public int MinRetainedCount;
        public bool ExpandWhenEmpty;
        public int CreatedCount;
        public readonly Queue<AvailableEntry> Available = new Queue<AvailableEntry>();
        public readonly List<PooledObject> Active = new List<PooledObject>();
    }

    private sealed class PoolGroup
    {
        public PoolBucket[] Buckets;
        public int NextVariantIndex;
        public int MaxRuntimeCreationsPerFrame;
        public int CreationFrame = -1;
        public int CreationsThisFrame;
        public int CurrentActiveCount;
        public int PeakActiveCount;
        public int RuntimeCreationCount;
        public int FailedRentCount;
        public int DestroyedCount;
        public int TotalCreatedCount;
        public int MaxTotalCount = int.MaxValue;
        public bool AutoShrink;
        public float IdleSecondsBeforeShrink;
        public float ShrinkCheckIntervalSeconds;
        public int MaxDestroysPerCheck;
        public float NextShrinkTime;
        public int NextShrinkBucket;
        public readonly Dictionary<string, int> VariantByName =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private readonly Dictionary<PoolId, PoolGroup> pools = new Dictionary<PoolId, PoolGroup>();
    private readonly List<PoolGroup> shrinkingGroups = new List<PoolGroup>();
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

            group.NextShrinkTime = now + group.ShrinkCheckIntervalSeconds;
            ShrinkGroup(group, now);
        }
    }

    private void Initialize()
    {
        if (initialized)
            return;

        if (groups == null || groups.Length == 0)
        {
            initialized = true;
            return;
        }

        bool needsResources = false;

        for (int i = 0; i < groups.Length; i++)
        {
            PoolGroupConfig config = groups[i];

            if (config != null && config.prefabSource == PoolPrefabSource.ResourcesFolder)
            {
                needsResources = true;
                break;
            }
        }

        if (needsResources && resourceManager == null)
            resourceManager = FindFirstObjectByType<ResourceManager>();

        if (needsResources && resourceManager == null)
        {
            return;
        }

        initialized = true;

        for (int i = 0; i < groups.Length; i++)
        {
            PoolGroupConfig config = groups[i];

            if (config == null)
                continue;

            PoolVariantConfig[] variants = ResolveVariants(config);

            if (variants == null || variants.Length == 0)
                continue;

            if (pools.ContainsKey(config.id))
            {
                continue;
            }

            PoolGroup group = new PoolGroup
            {
                Buckets = new PoolBucket[variants.Length],
                MaxRuntimeCreationsPerFrame = Mathf.Max(0, config.maxRuntimeCreationsPerFrame),
                MaxTotalCount = config.prefabSource == PoolPrefabSource.ResourcesFolder
                    ? Mathf.Max(1, config.resourceMaxCount)
                    : int.MaxValue,
                AutoShrink = config.enableAutoShrink,
                IdleSecondsBeforeShrink = Mathf.Max(1f, config.idleSecondsBeforeShrink),
                ShrinkCheckIntervalSeconds = Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds),
                MaxDestroysPerCheck = Mathf.Max(1, config.maxDestroysPerCheck),
                NextShrinkTime = Time.unscaledTime + Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds)
            };

            pools.Add(config.id, group);

            if (group.AutoShrink)
                shrinkingGroups.Add(group);

            for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                PoolVariantConfig variant = variants[variantIndex];

                if (variant == null || variant.prefab == null)
                    continue;

                PoolBucket bucket = new PoolBucket
                {
                    Prefab = variant.prefab,
                    Parent = config.parent != null ? config.parent : transform,
                    MaxCount = Mathf.Max(1, variant.maxCount),
                    MinRetainedCount = Mathf.Clamp(variant.minRetainedCount, 0, Mathf.Max(1, variant.maxCount)),
                    ExpandWhenEmpty = variant.expandWhenEmpty
                };

                group.Buckets[variantIndex] = bucket;

                if (group.VariantByName.ContainsKey(bucket.Prefab.name))
                    Debug.LogError("");
                else
                    group.VariantByName.Add(bucket.Prefab.name, variantIndex);

                int count = Mathf.Clamp(variant.initialCount, 0, bucket.MaxCount);

                for (int j = 0; j < count; j++)
                    CreateInstance(config.id, variantIndex, group, bucket);
            }
        }
    }

    private PoolVariantConfig[] ResolveVariants(PoolGroupConfig config)
    {
        if (config.prefabSource == PoolPrefabSource.Inspector)
            return config.variants;

        if (resourceManager == null)
        {
            return Array.Empty<PoolVariantConfig>();
        }

        GameObject[] prefabs = resourceManager.LoadPrefabGroup(config.id);

        if (prefabs == null || prefabs.Length == 0)
        {
            return Array.Empty<PoolVariantConfig>();
        }

        int maxTotal = Mathf.Max(1, config.resourceMaxCount);

        int initialTotal = Mathf.Clamp(config.resourceInitialCount, 0, maxTotal);
        PoolVariantConfig[] variants = new PoolVariantConfig[prefabs.Length];
        int initialBase = initialTotal / prefabs.Length;
        int initialRemainder = initialTotal % prefabs.Length;
        int minTotal = Mathf.Clamp(config.resourceMinRetainedCount, 0, maxTotal);
        int minBase = minTotal / prefabs.Length;
        int minRemainder = minTotal % prefabs.Length;

        for (int i = 0; i < prefabs.Length; i++)
        {
            variants[i] = new PoolVariantConfig
            {
                prefab = prefabs[i],
                initialCount = initialBase + (i < initialRemainder ? 1 : 0),
                maxCount = maxTotal,
                minRetainedCount = minBase + (i < minRemainder ? 1 : 0),
                expandWhenEmpty = config.resourceExpandWhenEmpty
            };
        }

        return variants;
    }

    private Transform GetInactiveInstantiationRoot()
    {
        if (inactiveInstantiationRoot != null)
            return inactiveInstantiationRoot;

        GameObject staging = new GameObject("PoolInactiveStaging");
        staging.SetActive(false);
        staging.transform.SetParent(transform, false);
        inactiveInstantiationRoot = staging.transform;

        return inactiveInstantiationRoot;
    }

    private PooledObject CreateInstance(PoolId id, int variantIndex, PoolGroup group, PoolBucket bucket)
    {
        if (bucket.CreatedCount >= bucket.MaxCount || group.TotalCreatedCount >= group.MaxTotalCount)
            return null;

        using (CreateInstanceMarker.Auto())
        {
            GameObject instance = Instantiate(bucket.Prefab, GetInactiveInstantiationRoot(), false);
            instance.name = bucket.Prefab.name;
            instance.SetActive(false);

            if (!instance.TryGetComponent(out PooledObject pooled))
                pooled = instance.AddComponent<PooledObject>();

            pooled.Bind(this, id, variantIndex);
            instance.transform.SetParent(bucket.Parent, false);

            bucket.CreatedCount++;
            group.TotalCreatedCount++;
            bucket.Available.Enqueue(new AvailableEntry(pooled, Time.unscaledTime));

            return pooled;
        }
    }

    public GameObject Rent(PoolId id, int variantIndex = -1)
    {
        return RentInternal(id, variantIndex, false, default, false, default);
    }

    public GameObject Rent(PoolId id, Vector3 position, int variantIndex = -1)
    {
        return RentInternal(id, variantIndex, true, position, false, default);
    }

    public GameObject Rent(PoolId id, Vector3 position, Quaternion rotation, int variantIndex = -1)
    {
        return RentInternal(id, variantIndex, true, position, true, rotation);
    }

    public bool TryGetVariantIndex(PoolId id, string prefabName, out int variantIndex)
    {
        Initialize();
        variantIndex = -1;

        return !string.IsNullOrEmpty(prefabName)
            && pools.TryGetValue(id, out PoolGroup group)
            && group.VariantByName.TryGetValue(prefabName, out variantIndex);
    }

    public GameObject Rent(PoolId id, string prefabName)
    {
        return TryGetVariantIndex(id, prefabName, out int index) ? Rent(id, index) : null;
    }

    public GameObject Rent(PoolId id, string prefabName, Vector3 position)
    {
        return TryGetVariantIndex(id, prefabName, out int index) ? Rent(id, position, index) : null;
    }

    public GameObject Rent(PoolId id, string prefabName, Vector3 position, Quaternion rotation)
    {
        return TryGetVariantIndex(id, prefabName, out int index) ? Rent(id, position, rotation, index) : null;
    }

    public int Prepare(PoolId id, string prefabName, int desiredAvailableCount)
    {
        if (!TryGetVariantIndex(id, prefabName, out int variantIndex))
            return 0;

        if (!pools.TryGetValue(id, out PoolGroup group))
            return 0;

        PoolBucket bucket = group.Buckets[variantIndex];

        if (bucket == null || !bucket.ExpandWhenEmpty)
            return 0;

        int created = 0;
        int target = Mathf.Max(0, desiredAvailableCount);

        while (bucket.Available.Count < target && CanCreateThisFrame(group))
        {
            if (CreateInstance(id, variantIndex, group, bucket) == null)
                break;

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

        return group.MaxRuntimeCreationsPerFrame == 0 ||
            group.CreationsThisFrame < group.MaxRuntimeCreationsPerFrame;
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

            GameObject selected = RentFromBucket(id, variantIndex, group, group.Buckets[variantIndex], hasPosition, position, hasRotation, rotation);

            if (selected == null)
                group.FailedRentCount++;

            return selected;
        }

        int count = group.Buckets.Length;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (group.NextVariantIndex + offset) % count;
            GameObject instance = RentFromBucket(id, index, group, group.Buckets[index], hasPosition, position, hasRotation, rotation);

            if (instance == null)
                continue;

            group.NextVariantIndex = (index + 1) % count;
            return instance;
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

            bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
            group.TotalCreatedCount = Mathf.Max(0, group.TotalCreatedCount - 1);
        }

        if (pooled == null && bucket.ExpandWhenEmpty && bucket.CreatedCount < bucket.MaxCount)
        {
            if (CanCreateThisFrame(group))
            {
                PooledObject created = CreateInstance(id, variantIndex, group, bucket);

                if (created != null)
                {
                    pooled = bucket.Available.Dequeue().Pooled;
                    group.CreationsThisFrame++;
                    group.RuntimeCreationCount++;
                }
            }
        }

        if (pooled == null)
            return null;

        Transform instanceTransform = pooled.transform;

        if (hasPosition && hasRotation)
            instanceTransform.SetPositionAndRotation(position, rotation);
        else if (hasPosition)
            instanceTransform.position = position;

        pooled.IsRented = true;
        pooled.ActiveIndex = bucket.Active.Count;
        bucket.Active.Add(pooled);
        group.CurrentActiveCount++;
        group.PeakActiveCount = Mathf.Max(group.PeakActiveCount, group.CurrentActiveCount);
        pooled.gameObject.SetActive(true);

        return pooled.gameObject;
    }

    public bool Return(GameObject instance)
    {
        if (instance == null || !instance.TryGetComponent(out PooledObject pooled))
            return false;

        if (pooled.Owner != this || !pooled.IsRented)
            return false;

        if (!pools.TryGetValue(pooled.PoolId, out PoolGroup group))
            return false;

        int variantIndex = pooled.VariantIndex;

        if (variantIndex < 0 || variantIndex >= group.Buckets.Length)
            return false;

        PoolBucket bucket = group.Buckets[variantIndex];

        if (bucket == null)
            return false;

        int activeIndex = pooled.ActiveIndex;
        int lastIndex = bucket.Active.Count - 1;

        if (activeIndex < 0 || activeIndex > lastIndex || bucket.Active[activeIndex] != pooled)
            return false;

        PooledObject last = bucket.Active[lastIndex];
        bucket.Active[activeIndex] = last;
        last.ActiveIndex = activeIndex;
        bucket.Active.RemoveAt(lastIndex);
        group.CurrentActiveCount = Mathf.Max(0, group.CurrentActiveCount - 1);

        pooled.IsRented = false;
        pooled.ActiveIndex = -1;
        instance.SetActive(false);

        if (instance.transform.parent != bucket.Parent)
            instance.transform.SetParent(bucket.Parent, false);

        bucket.Available.Enqueue(new AvailableEntry(pooled, Time.unscaledTime));
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
                PooledObject pooled = bucket.Active[bucket.Active.Count - 1];

                if (pooled == null)
                {
                    bucket.Active.RemoveAt(bucket.Active.Count - 1);
                    bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
                    group.TotalCreatedCount = Mathf.Max(0, group.TotalCreatedCount - 1);
                    group.CurrentActiveCount = Mathf.Max(0, group.CurrentActiveCount - 1);
                    continue;
                }

                if (!Return(pooled.gameObject))
                    bucket.Active.RemoveAt(bucket.Active.Count - 1);
            }
        }
    }

    private void ShrinkGroup(PoolGroup group, float now)
    {
        int bucketCount = group.Buckets.Length;

        if (bucketCount == 0)
            return;

        int destroyed = 0;
        int attemptsWithoutProgress = 0;

        while (destroyed < group.MaxDestroysPerCheck && attemptsWithoutProgress < bucketCount)
        {
            int index = group.NextShrinkBucket;
            group.NextShrinkBucket = (group.NextShrinkBucket + 1) % bucketCount;

            PoolBucket bucket = group.Buckets[index];

            if (bucket != null && TryShrinkBucket(group, bucket, now))
            {
                destroyed++;
                attemptsWithoutProgress = 0;
            }
            else
            {
                attemptsWithoutProgress++;
            }
        }
    }

    private bool TryShrinkBucket(PoolGroup group, PoolBucket bucket, float now)
    {
        while (bucket.Available.Count > 0 && bucket.Available.Peek().Pooled == null)
        {
            bucket.Available.Dequeue();
            bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
            group.TotalCreatedCount = Mathf.Max(0, group.TotalCreatedCount - 1);
        }

        if (bucket.CreatedCount <= bucket.MinRetainedCount || bucket.Available.Count == 0)
            return false;

        AvailableEntry oldest = bucket.Available.Peek();

        if (now - oldest.IdleSince < group.IdleSecondsBeforeShrink)
            return false;

        bucket.Available.Dequeue();
        bucket.CreatedCount--;
        group.TotalCreatedCount--;
        group.DestroyedCount++;
        Destroy(oldest.Pooled.gameObject);
        return true;
    }

    public int GetDestroyedCount(PoolId id)
    {
        Initialize();
        return pools.TryGetValue(id, out PoolGroup group) ? group.DestroyedCount : 0;
    }

    public int GetVariantCount(PoolId id)
    {
        Initialize();
        return pools.TryGetValue(id, out PoolGroup group) ? group.Buckets.Length : 0;
    }

    public int GetActiveCount(PoolId id)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group))
            return 0;

        return group.CurrentActiveCount;
    }

    public int GetCreatedCount(PoolId id)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group))
            return 0;

        return group.TotalCreatedCount;
    }

    public int GetCreatedCount(PoolId id, int variantIndex)
    {
        Initialize();

        if (!pools.TryGetValue(id, out PoolGroup group) ||
            variantIndex < 0 || variantIndex >= group.Buckets.Length)
            return 0;

        PoolBucket bucket = group.Buckets[variantIndex];
        return bucket != null ? bucket.CreatedCount : 0;
    }

    public int GetPeakActiveCount(PoolId id)
    {
        Initialize();
        return pools.TryGetValue(id, out PoolGroup group) ? group.PeakActiveCount : 0;
    }

    public int GetRuntimeCreationCount(PoolId id)
    {
        Initialize();
        return pools.TryGetValue(id, out PoolGroup group) ? group.RuntimeCreationCount : 0;
    }

    public int GetFailedRentCount(PoolId id)
    {
        Initialize();
        return pools.TryGetValue(id, out PoolGroup group) ? group.FailedRentCount : 0;
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
}
