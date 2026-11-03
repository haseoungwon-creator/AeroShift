using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public sealed class PoolManager : MonoBehaviour
{
    public enum PoolPrefabSource
    {
        Inspector,
        ResourcesFolder
    }

    [Serializable]
    public sealed class PoolVariantConfig
    {
        public GameObject prefab;
        [Min(0)] public int initialCount = 0;
        [Min(1)] public int maxCount = 10;
        public bool expandWhenEmpty;
    }

    [Serializable]
    public sealed class PoolGroupConfig
    {
        public PoolId id;
        public Transform parent;
        public PoolPrefabSource prefabSource = PoolPrefabSource.ResourcesFolder;
        [HideInInspector] public string resourcesFolder;
        [Min(0)] public int resourceInitialCount;
        [Min(1)] public int resourceMaxCount = 32;
        public bool resourceExpandWhenEmpty = true;
        public PoolVariantConfig[] variants;
        [Min(0)] public int maxRuntimeCreationsPerFrame;
    }

    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private PoolGroupConfig[] groups;

    private sealed class PoolBucket
    {
        public GameObject Prefab;
        public Transform Parent;
        public int MaxCount;
        public bool ExpandWhenEmpty;
        public int CreatedCount;
        public readonly Queue<PooledObject> Available = new Queue<PooledObject>();
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
    }

    private readonly Dictionary<PoolId, PoolGroup> pools = new Dictionary<PoolId, PoolGroup>();
    private bool initialized;

    private void Awake()
    {
        Initialize();
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
            Debug.LogError("Resources 풀을 사용하려면 씬에 ResourceManager가 필요합니다.", this);
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
                Debug.LogError($"PoolId가 중복되었습니다: {config.id}", this);
                continue;
            }

            PoolGroup group = new PoolGroup
            {
                Buckets = new PoolBucket[variants.Length],
                MaxRuntimeCreationsPerFrame = Mathf.Max(0, config.maxRuntimeCreationsPerFrame)
            };

            pools.Add(config.id, group);

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
                    ExpandWhenEmpty = variant.expandWhenEmpty
                };

                group.Buckets[variantIndex] = bucket;

                int count = Mathf.Clamp(variant.initialCount, 0, bucket.MaxCount);

                for (int j = 0; j < count; j++)
                    CreateInstance(config.id, variantIndex, bucket);
            }
        }
    }

    private PoolVariantConfig[] ResolveVariants(PoolGroupConfig config)
    {
        if (config.prefabSource == PoolPrefabSource.Inspector)
            return config.variants;

        if (resourceManager == null)
        {
            Debug.LogError($"{config.id}의 ResourceManager가 연결되지 않았습니다.", this);
            return Array.Empty<PoolVariantConfig>();
        }

        GameObject[] prefabs = resourceManager.LoadPrefabGroup(config.id);

        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning($"{config.id}에 등록된 프리팹이 없습니다.", this);
            return Array.Empty<PoolVariantConfig>();
        }

        int maxTotal = Mathf.Max(1, config.resourceMaxCount);

        if (maxTotal < prefabs.Length)
        {
            Debug.LogError($"{config.id} 최대 생성 수가 프리팹 개수보다 작습니다: {maxTotal} / {prefabs.Length}", this);
            return Array.Empty<PoolVariantConfig>();
        }

        int initialTotal = Mathf.Clamp(config.resourceInitialCount, 0, maxTotal);
        PoolVariantConfig[] variants = new PoolVariantConfig[prefabs.Length];
        int initialBase = initialTotal / prefabs.Length;
        int initialRemainder = initialTotal % prefabs.Length;
        int maxBase = maxTotal / prefabs.Length;
        int maxRemainder = maxTotal % prefabs.Length;

        for (int i = 0; i < prefabs.Length; i++)
        {
            variants[i] = new PoolVariantConfig
            {
                prefab = prefabs[i],
                initialCount = initialBase + (i < initialRemainder ? 1 : 0),
                maxCount = maxBase + (i < maxRemainder ? 1 : 0),
                expandWhenEmpty = config.resourceExpandWhenEmpty
            };
        }

        Debug.Log($"{config.id} 프리팹 {prefabs.Length}종을 ResourceManager에서 등록했습니다.", this);
        return variants;
    }

    private PooledObject CreateInstance(PoolId id, int variantIndex, PoolBucket bucket)
    {
        if (bucket.CreatedCount >= bucket.MaxCount)
            return null;

        GameObject instance = Instantiate(bucket.Prefab, bucket.Parent, false);
        instance.name = bucket.Prefab.name;
        instance.SetActive(false);

        if (!instance.TryGetComponent(out PooledObject pooled))
            pooled = instance.AddComponent<PooledObject>();

        pooled.Bind(this, id, variantIndex);
        bucket.CreatedCount++;
        bucket.Available.Enqueue(pooled);

        return pooled;
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
            pooled = bucket.Available.Dequeue();

            if (pooled != null)
                break;

            bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
        }

        if (pooled == null && bucket.ExpandWhenEmpty && bucket.CreatedCount < bucket.MaxCount)
        {
            if (group.CreationFrame != Time.frameCount)
            {
                group.CreationFrame = Time.frameCount;
                group.CreationsThisFrame = 0;
            }

            if (group.MaxRuntimeCreationsPerFrame == 0 ||
                group.CreationsThisFrame < group.MaxRuntimeCreationsPerFrame)
            {
                PooledObject created = CreateInstance(id, variantIndex, bucket);

                if (created != null)
                {
                    pooled = bucket.Available.Dequeue();
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

        bucket.Available.Enqueue(pooled);
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
                    group.CurrentActiveCount = Mathf.Max(0, group.CurrentActiveCount - 1);
                    continue;
                }

                if (!Return(pooled.gameObject))
                    bucket.Active.RemoveAt(bucket.Active.Count - 1);
            }
        }
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

        int total = 0;

        for (int i = 0; i < group.Buckets.Length; i++)
        {
            if (group.Buckets[i] != null)
                total += group.Buckets[i].CreatedCount;
        }

        return total;
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
