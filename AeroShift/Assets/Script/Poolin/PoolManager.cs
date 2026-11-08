
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class PoolManager : MonoBehaviour
{
    private static readonly ProfilerMarker CreateMarker =
        new ProfilerMarker("AeroShift.Pool.CreateInstance");

    public enum PoolPrefabSource
    {
        Inspector,
        ResourcesFolder
    }

    [Serializable]
    public sealed class PoolVariantConfig
    {
        public GameObject prefab;
        [Min(0)] public int initialCount;
        [Min(1)] public int maxCount = 10;
        public bool expandWhenEmpty = true;
        [Min(0)] public int minRetainedCount;
    }

    [Serializable]
    public sealed class PoolGroupConfig
    {
        public PoolId id;
        public Transform parent;
        public PoolPrefabSource prefabSource =
            PoolPrefabSource.ResourcesFolder;

        [HideInInspector] public string resourcesFolder;

        [Min(0)] public int resourceInitialCount;
        [Min(1)] public int resourceMaxCount = 32;
        public bool resourceExpandWhenEmpty = true;

        public PoolVariantConfig[] variants;

        [Min(0)] public int maxRuntimeCreationsPerFrame = 2;

        public bool enableAutoShrink;
        [Min(0)] public int resourceMinRetainedCount;
        [Min(1f)] public float idleSecondsBeforeShrink = 60f;
        [Min(0.1f)] public float shrinkCheckIntervalSeconds = 10f;
        [Min(1)] public int maxDestroysPerCheck = 2;
    }

    [Header("References")]
    [SerializeField] private ResourceManager resourceManager;

    [Header("Pool Groups")]
    [SerializeField] private PoolGroupConfig[] groups;

    [Header("Automatic Registration")]
    [SerializeField]
    private bool autoRegisterMissingResourceGroups = true;

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

        public readonly Queue<AvailableEntry> Available =
            new Queue<AvailableEntry>();

        public readonly List<PooledObject> Active =
            new List<PooledObject>();
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
        public int TotalCreatedCount;
        public int MaxTotalCount;

        public int RuntimeCreationCount;
        public int FailedRentCount;
        public int DestroyedCount;

        public bool AutoShrink;
        public float IdleSecondsBeforeShrink;
        public float ShrinkCheckIntervalSeconds;
        public float NextShrinkTime;
        public int MaxDestroysPerCheck;
        public int NextShrinkBucket;

        public readonly Dictionary<string, int> VariantByName =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private readonly Dictionary<PoolId, PoolGroup> pools =
        new Dictionary<PoolId, PoolGroup>();

    private readonly List<PoolGroup> shrinkingGroups =
        new List<PoolGroup>();

    private Transform inactiveInstantiationRoot;
    private bool initialized;
    private bool initializing;

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
        if (initialized || initializing)
            return;

        initializing = true;

        if (resourceManager == null)
            resourceManager = FindFirstObjectByType<ResourceManager>();

        if (resourceManager == null &&
            autoRegisterMissingResourceGroups)
        {
            Debug.LogError(
                "PoolManager에 ResourceManager가 필요합니다.",
                this
            );

            initializing = false;
            return;
        }

        if (groups != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                PoolGroupConfig config = groups[i];

                if (config == null)
                    continue;

                if (pools.ContainsKey(config.id))
                {
                    Debug.LogWarning(
                        $"중복 PoolId: {config.id}",
                        this
                    );
                    continue;
                }

                if (config.prefabSource ==
                        PoolPrefabSource.Inspector &&
                    !HasValidInspectorVariant(config) &&
                    autoRegisterMissingResourceGroups)
                {
                    PoolGroupConfig fallback =
                        CreateDefaultConfig(config.id);

                    fallback.parent = config.parent;
                    RegisterGroup(fallback);
                }
                else
                {
                    RegisterGroup(config);
                }
            }
        }

        if (autoRegisterMissingResourceGroups)
        {
            RegisterMissing(PoolId.Cloud);
            RegisterMissing(PoolId.NormalMap);
            RegisterMissing(PoolId.SafeMap);
        }

        initialized = true;
        initializing = false;

        ValidateRequiredGroups();
    }

    private static bool HasValidInspectorVariant(
        PoolGroupConfig config)
    {
        if (config.variants == null)
            return false;

        for (int i = 0; i < config.variants.Length; i++)
        {
            if (config.variants[i] != null &&
                config.variants[i].prefab != null)
                return true;
        }

        return false;
    }

    private void RegisterMissing(PoolId id)
    {
        if (pools.ContainsKey(id))
            return;

        if (resourceManager == null ||
            !resourceManager.TryGetGroupPath(id, out _))
            return;

        RegisterGroup(CreateDefaultConfig(id));
    }

    private static PoolGroupConfig CreateDefaultConfig(PoolId id)
    {
        PoolGroupConfig config = new PoolGroupConfig
        {
            id = id,
            prefabSource = PoolPrefabSource.ResourcesFolder,
            resourceInitialCount = 0,
            resourceMaxCount = 32,
            resourceExpandWhenEmpty = true,
            maxRuntimeCreationsPerFrame = 2
        };

        switch (id)
        {
            case PoolId.Cloud:
                config.resourceInitialCount = 300;
                config.resourceMaxCount = 600;
                break;

            case PoolId.NormalMap:
                config.resourceInitialCount = 0;
                config.resourceMaxCount = 64;
                break;

            case PoolId.SafeMap:
                config.resourceInitialCount = 3;
                config.resourceMaxCount = 16;
                break;
        }

        return config;
    }

    private void RegisterGroup(PoolGroupConfig config)
    {
        PoolVariantConfig[] variants = ResolveVariants(config);

        if (variants == null || variants.Length == 0)
        {
            Debug.LogWarning(
                $"{config.id} 그룹에 로드된 프리팹이 없습니다.",
                this
            );
            return;
        }

        bool useResources =
            config.prefabSource == PoolPrefabSource.ResourcesFolder;

        PoolGroup group = new PoolGroup
        {
            Buckets = new PoolBucket[variants.Length],

            MaxTotalCount = useResources
                ? Mathf.Max(1, config.resourceMaxCount)
                : int.MaxValue,

            MaxRuntimeCreationsPerFrame =
                Mathf.Max(0, config.maxRuntimeCreationsPerFrame),

            AutoShrink = config.enableAutoShrink,

            IdleSecondsBeforeShrink =
                Mathf.Max(1f, config.idleSecondsBeforeShrink),

            ShrinkCheckIntervalSeconds =
                Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds),

            MaxDestroysPerCheck =
                Mathf.Max(1, config.maxDestroysPerCheck),

            NextShrinkTime = Time.unscaledTime +
                Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds)
        };

        pools.Add(config.id, group);

        if (group.AutoShrink)
            shrinkingGroups.Add(group);

        for (int i = 0; i < variants.Length; i++)
        {
            PoolVariantConfig variant = variants[i];

            if (variant == null || variant.prefab == null)
                continue;

            PoolBucket bucket = new PoolBucket
            {
                Prefab = variant.prefab,

                Parent = config.parent != null
                    ? config.parent
                    : transform,

                MaxCount = Mathf.Max(1, variant.maxCount),

                MinRetainedCount = Mathf.Clamp(
                    variant.minRetainedCount,
                    0,
                    Mathf.Max(1, variant.maxCount)
                ),

                ExpandWhenEmpty = variant.expandWhenEmpty
            };

            group.Buckets[i] = bucket;

            if (group.VariantByName.ContainsKey(bucket.Prefab.name))
            {
                Debug.LogError(
                    $"{config.id}에 중복 프리팹 이름: " +
                    bucket.Prefab.name,
                    this
                );
            }
            else
            {
                group.VariantByName.Add(
                    bucket.Prefab.name,
                    i
                );
            }

            int initialCount = Mathf.Clamp(
                variant.initialCount,
                0,
                bucket.MaxCount
            );

            for (int j = 0; j < initialCount; j++)
            {
                if (CreateInstance(config.id, i, group, bucket) == null)
                    break;
            }
        }

        Debug.Log(
            $"PoolManager 등록: {config.id}, " +
            $"프리팹 {group.VariantByName.Count}종, " +
            $"생성 {group.TotalCreatedCount}개",
            this
        );
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

        int maxTotal = Mathf.Max(1, config.resourceMaxCount);
        int initialTotal = Mathf.Clamp(
            config.resourceInitialCount, 0, maxTotal);

        int retainedTotal = Mathf.Clamp(
            config.resourceMinRetainedCount, 0, maxTotal);

        int initialBase = initialTotal / prefabs.Length;
        int initialRemainder = initialTotal % prefabs.Length;

        int retainedBase = retainedTotal / prefabs.Length;
        int retainedRemainder = retainedTotal % prefabs.Length;

        PoolVariantConfig[] variants =
            new PoolVariantConfig[prefabs.Length];

        for (int i = 0; i < prefabs.Length; i++)
        {
            variants[i] = new PoolVariantConfig
            {
                prefab = prefabs[i],

                initialCount = initialBase +
                    (i < initialRemainder ? 1 : 0),

                maxCount = maxTotal,

                minRetainedCount = retainedBase +
                    (i < retainedRemainder ? 1 : 0),

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

    private PooledObject CreateInstance(
        PoolId id,
        int variantIndex,
        PoolGroup group,
        PoolBucket bucket)
    {
        if (bucket == null ||
            bucket.CreatedCount >= bucket.MaxCount ||
            group.TotalCreatedCount >= group.MaxTotalCount)
            return null;

        using (CreateMarker.Auto())
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
    }

    public GameObject Rent(
        PoolId id,
        int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, false, default, false, default);
    }

    public GameObject Rent(
        PoolId id,
        Vector3 position,
        int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, true, position, false, default);
    }

    public GameObject Rent(
        PoolId id,
        Vector3 position,
        Quaternion rotation,
        int variantIndex = -1)
    {
        return RentInternal(
            id, variantIndex, true, position, true, rotation);
    }

    public GameObject Rent(
        PoolId id,
        string prefabName)
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

        return !string.IsNullOrEmpty(prefabName) &&
            pools.TryGetValue(id, out PoolGroup group) &&
            group.VariantByName.TryGetValue(
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
        int target = Mathf.Max(0, desiredAvailableCount);

        while (bucket.Available.Count < target &&
               CanCreateThisFrame(group))
        {
            if (CreateInstance(id, index, group, bucket) == null)
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
            group.CreationsThisFrame <
            group.MaxRuntimeCreationsPerFrame;
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

        for (int offset = 0; offset < group.Buckets.Length; offset++)
        {
            int index = (group.NextVariantIndex + offset) %
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
            PooledObject created = CreateInstance(
                id,
                variantIndex,
                group,
                bucket
            );

            if (created != null)
            {
                pooled = bucket.Available.Dequeue().Pooled;
                group.CreationsThisFrame++;
                group.RuntimeCreationCount++;
            }
        }

        if (pooled == null)
            return null;

        Transform target = pooled.transform;

        if (hasPosition && hasRotation)
            target.SetPositionAndRotation(position, rotation);
        else if (hasPosition)
            target.position = position;

        pooled.IsRented = true;
        pooled.ActiveIndex = bucket.Active.Count;

        bucket.Active.Add(pooled);
        group.CurrentActiveCount++;

        group.PeakActiveCount = Mathf.Max(
            group.PeakActiveCount,
            group.CurrentActiveCount
        );

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

        if (!pools.TryGetValue(pooled.PoolId, out PoolGroup group))
            return false;

        int index = pooled.VariantIndex;

        if (index < 0 || index >= group.Buckets.Length)
            return false;

        PoolBucket bucket = group.Buckets[index];

        if (bucket == null)
            return false;

        int activeIndex = pooled.ActiveIndex;
        int lastIndex = bucket.Active.Count - 1;

        if (activeIndex < 0 ||
            activeIndex > lastIndex ||
            bucket.Active[activeIndex] != pooled)
            return false;

        PooledObject last = bucket.Active[lastIndex];

        bucket.Active[activeIndex] = last;
        last.ActiveIndex = activeIndex;

        bucket.Active.RemoveAt(lastIndex);

        group.CurrentActiveCount =
            Mathf.Max(0, group.CurrentActiveCount - 1);

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
        Initialize();

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

                if (pooled == null)
                {
                    bucket.Active.RemoveAt(bucket.Active.Count - 1);

                    bucket.CreatedCount =
                        Mathf.Max(0, bucket.CreatedCount - 1);

                    group.TotalCreatedCount =
                        Mathf.Max(0, group.TotalCreatedCount - 1);

                    group.CurrentActiveCount =
                        Mathf.Max(0, group.CurrentActiveCount - 1);

                    continue;
                }

                if (!Return(pooled.gameObject))
                    break;
            }
        }
    }

    private void ShrinkGroup(PoolGroup group, float now)
    {
        if (group.Buckets.Length == 0)
            return;

        int destroyed = 0;
        int attempts = 0;

        while (destroyed < group.MaxDestroysPerCheck &&
               attempts < group.Buckets.Length)
        {
            int index = group.NextShrinkBucket;

            group.NextShrinkBucket =
                (index + 1) % group.Buckets.Length;

            PoolBucket bucket = group.Buckets[index];

            if (bucket != null &&
                TryShrinkBucket(group, bucket, now))
            {
                destroyed++;
                attempts = 0;
            }
            else
            {
                attempts++;
            }
        }
    }

    private static bool CanShrinkBucket(PoolBucket bucket)
    {
        return bucket.Available.Count > 0 &&
            bucket.CreatedCount > bucket.MinRetainedCount;
    }

    private bool TryShrinkBucket(
        PoolGroup group,
        PoolBucket bucket,
        float now)
    {
        if (!CanShrinkBucket(bucket))
            return false;

        AvailableEntry oldest = bucket.Available.Peek();

        if (now - oldest.IdleSince <
            group.IdleSecondsBeforeShrink)
            return false;

        bucket.Available.Dequeue();

        bucket.CreatedCount--;
        group.TotalCreatedCount--;
        group.DestroyedCount++;

        if (oldest.Pooled != null)
            Destroy(oldest.Pooled.gameObject);

        return true;
    }

    private void ValidateRequiredGroups()
    {
        if (!pools.ContainsKey(PoolId.Cloud))
            Debug.LogError(
                "Cloud 그룹 등록 실패: ResourceManager 경로와 " +
                "Resources 프리팹을 확인하세요.",
                this
            );

        if (!pools.ContainsKey(PoolId.NormalMap))
            Debug.LogError(
                "NormalMap 그룹 등록 실패: ResourceManager 경로와 " +
                "Resources 프리팹을 확인하세요.",
                this
            );
    }

    public int GetVariantCount(PoolId id)
    {
        Initialize();

        return pools.TryGetValue(id, out PoolGroup group)
            ? group.VariantByName.Count
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
