using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public sealed class PoolManager : MonoBehaviour
{
    [Serializable]
    public class PoolGroupConfig
    {
        public PoolId id;
        public Transform parent;
        public int resourceInitialCount;
        public bool resourceExpandWhenEmpty = true;
        public bool initializeOnFirstUse;
        public bool enableAutoShrink;
        public float idleSecondsBeforeShrink = 60f;
        public float shrinkCheckIntervalSeconds = 10f;
    }

    private struct AvailableObject
    {
        public PooledObject Object;
        public float ReturnedAt;

        public AvailableObject(PooledObject pooled)
        {
            Object = pooled;
            ReturnedAt = Time.unscaledTime;
        }
    }

    private class PoolBucket
    {
        public GameObject Prefab;
        public Transform Parent;
        public int InitialCount;
        public int CreatedCount;
        public bool Expand;
        public readonly Queue<AvailableObject> Available = new Queue<AvailableObject>();
        public readonly List<PooledObject> Active = new List<PooledObject>();
    }

    private class PoolGroup
    {
        public PoolBucket[] Buckets;
        public readonly Dictionary<string, int> Names = new Dictionary<string, int>();
        public int NextVariant;
        public int CreationFrame = -1;
        public int CreationsThisFrame;
        public int ActiveCount;
        public int NextShrinkBucket;
        public float NextShrinkTime;
        public float ShrinkInterval;
        public float ShrinkIdleTime;
        public bool AutoShrink;
    }

    [Header("Core JSON Balance (Resources/Config/Settings/PoolManager)")]
    [SerializeField] private TextAsset settingsJson;
    private bool settingsLoaded;

    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private PoolGroupConfig[] groups;
    [SerializeField] private bool shrinkDuringGameplay;
    [SerializeField] private LayerMask prewarmObstacleLayers = 1;

    private readonly Dictionary<PoolId, PoolGroup> pools = new Dictionary<PoolId, PoolGroup>();
    private readonly Dictionary<PoolId, PoolGroupConfig> deferred = new Dictionary<PoolId, PoolGroupConfig>();
    private Transform inactiveRoot;
    private bool initialized;

    private void Awake()
    {
        LoadSettings();
        Initialize();
    }

    private void Update()
    {
        if (!shrinkDuringGameplay && GameManager.IsPlaying)
            return;

        float now = Time.unscaledTime;
        foreach (PoolGroup group in pools.Values)
        {
            if (!group.AutoShrink || now < group.NextShrinkTime)
                continue;

            group.NextShrinkTime = now + group.ShrinkInterval;
            Shrink(group, now);
        }
    }

    private void Initialize()
    {
        LoadSettings();
        if (initialized)
            return;

        initialized = true;
        if (resourceManager == null)
            resourceManager = FindFirstObjectByType<ResourceManager>();

        if (groups == null)
            return;

        for (int i = 0; i < groups.Length; i++)
        {
            PoolGroupConfig config = groups[i];
            if (config == null || pools.ContainsKey(config.id) || deferred.ContainsKey(config.id))
                continue;

            if (config.initializeOnFirstUse)
                deferred.Add(config.id, config);
            else
                CreateGroup(config, true);
        }
    }

    private void EnsureGroup(PoolId id)
    {
        Initialize();
        if (!deferred.TryGetValue(id, out PoolGroupConfig config))
            return;

        deferred.Remove(id);
        CreateGroup(config, false);
    }

    private void CreateGroup(PoolGroupConfig config, bool prewarm)
    {
        if (resourceManager == null)
            return;
        GameObject[] prefabs = resourceManager.LoadPrefabGroup(config.id);
        if (prefabs == null || prefabs.Length == 0)
            return;

        PoolGroup group = new PoolGroup();
        group.Buckets = new PoolBucket[prefabs.Length];
        group.AutoShrink = config.enableAutoShrink;
        group.ShrinkInterval = Mathf.Max(0.1f, config.shrinkCheckIntervalSeconds);
        group.ShrinkIdleTime = Mathf.Max(1f, config.idleSecondsBeforeShrink);
        group.NextShrinkTime = Time.unscaledTime + group.ShrinkInterval;
        pools.Add(config.id, group);

        int initialCount = Mathf.Max(0, config.resourceInitialCount);
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null)
                continue;

            PoolBucket bucket = new PoolBucket();
            bucket.Prefab = prefab;
            bucket.Parent = config.parent != null ? config.parent : transform;
            bucket.InitialCount = initialCount / prefabs.Length + (i < initialCount % prefabs.Length ? 1 : 0);
            bucket.Expand = config.resourceExpandWhenEmpty;
            group.Buckets[i] = bucket;

            if (!group.Names.ContainsKey(prefab.name))
                group.Names.Add(prefab.name, i);

            if (prewarm)
            {
                for (int j = 0; j < bucket.InitialCount; j++)
                    CreateObject(config.id, i, bucket);
            }
        }
    }

    private PooledObject CreateObject(PoolId id, int variant, PoolBucket bucket)
    {
        if (inactiveRoot == null)
        {
            GameObject root = new GameObject("PoolInactiveStaging");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            inactiveRoot = root.transform;
        }

        GameObject instance = Instantiate(bucket.Prefab, inactiveRoot, false);
        instance.name = bucket.Prefab.name;
        instance.SetActive(false);

        if (!instance.TryGetComponent(out PooledObject pooled))
            pooled = instance.AddComponent<PooledObject>();
        pooled.Bind(this, id, variant);

        if (id == PoolId.NormalMap || id == PoolId.SafeMap)
        {
            if (!instance.TryGetComponent(out MapChunk chunk))
                chunk = instance.AddComponent<MapChunk>();
            if (id == PoolId.NormalMap)
                chunk.GetCollisionGroup(prewarmObstacleLayers);
        }

        instance.transform.SetParent(bucket.Parent, false);
        bucket.CreatedCount++;
        bucket.Available.Enqueue(new AvailableObject(pooled));
        return pooled;
    }

    private bool CanCreate(PoolGroup group)
    {
        if (group.CreationFrame != Time.frameCount)
        {
            group.CreationFrame = Time.frameCount;
            group.CreationsThisFrame = 0;
        }
        return group.CreationsThisFrame < 2;
    }

    public bool IsGroupConfigured(PoolId id)
    {
        Initialize();
        if (pools.ContainsKey(id))
            return true;
        return deferred.ContainsKey(id) && resourceManager != null &&
            resourceManager.TryGetGroupPath(id, out _);
    }

    public int GetVariantCount(PoolId id)
    {
        EnsureGroup(id);
        return pools.TryGetValue(id, out PoolGroup group) ? group.Buckets.Length : 0;
    }

    public bool TryGetVariantIndex(PoolId id, string prefabName, out int variantIndex)
    {
        EnsureGroup(id);
        variantIndex = -1;
        return !string.IsNullOrEmpty(prefabName) &&
            pools.TryGetValue(id, out PoolGroup group) &&
            group.Names.TryGetValue(prefabName, out variantIndex);
    }

    public bool PrewarmInitialCount(PoolId id)
    {
        EnsureGroup(id);
        if (!pools.TryGetValue(id, out PoolGroup group))
            return false;

        bool ready = true;
        for (int i = 0; i < group.Buckets.Length; i++)
        {
            PoolBucket bucket = group.Buckets[i];
            if (bucket == null)
            {
                ready = false;
                continue;
            }
            if (bucket.Available.Count < bucket.InitialCount)
                Prepare(id, i, bucket.InitialCount);
            if (bucket.Available.Count < bucket.InitialCount)
                ready = false;
        }
        return ready;
    }

    public int Prepare(PoolId id, int variant, int desiredAvailableCount)
    {
        EnsureGroup(id);
        if (!pools.TryGetValue(id, out PoolGroup group) || variant < 0 || variant >= group.Buckets.Length)
            return 0;

        PoolBucket bucket = group.Buckets[variant];
        if (bucket == null || !bucket.Expand)
            return 0;

        int created = 0;
        while (bucket.Available.Count < desiredAvailableCount && CanCreate(group))
        {
            CreateObject(id, variant, bucket);
            group.CreationsThisFrame++;
            created++;
        }
        return created;
    }

    public GameObject RentInactive(PoolId id, Vector3 position, Transform parent, int variantIndex = -1)
    {
        return RentInternal(id, variantIndex, position, Quaternion.identity, false, parent);
    }

    public GameObject RentInactive(PoolId id, Vector3 position, Quaternion rotation, Transform parent, int variantIndex = -1)
    {
        return RentInternal(id, variantIndex, position, rotation, true, parent);
    }

    private GameObject RentInternal(PoolId id, int variant, Vector3 position, Quaternion rotation,
        bool hasRotation, Transform parent)
    {
        EnsureGroup(id);
        if (!pools.TryGetValue(id, out PoolGroup group))
            return null;

        if (variant >= 0)
        {
            if (variant >= group.Buckets.Length)
                return null;
            return RentFromBucket(id, variant, group, group.Buckets[variant], position, rotation, hasRotation, parent);
        }

        for (int i = 0; i < group.Buckets.Length; i++)
        {
            int index = (group.NextVariant + i) % group.Buckets.Length;
            GameObject result = RentFromBucket(id, index, group, group.Buckets[index],
                position, rotation, hasRotation, parent);
            if (result == null)
                continue;
            group.NextVariant = (index + 1) % group.Buckets.Length;
            return result;
        }
        return null;
    }

    private GameObject RentFromBucket(PoolId id, int variant, PoolGroup group, PoolBucket bucket,
        Vector3 position, Quaternion rotation, bool hasRotation, Transform parent)
    {
        if (bucket == null)
            return null;

        PooledObject pooled = null;
        while (bucket.Available.Count > 0)
        {
            pooled = bucket.Available.Dequeue().Object;
            if (pooled != null)
                break;
            bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
        }

        if (pooled == null && bucket.Expand && CanCreate(group))
        {
            pooled = CreateObject(id, variant, bucket);
            bucket.Available.Dequeue();
            group.CreationsThisFrame++;
        }
        if (pooled == null)
            return null;

        Transform target = pooled.transform;
        if (parent != null && target.parent != parent)
            target.SetParent(parent, true);
        if (hasRotation)
            target.SetPositionAndRotation(position, rotation);
        else
            target.position = position;

        pooled.IsRented = true;
        pooled.ActiveIndex = bucket.Active.Count;
        bucket.Active.Add(pooled);
        group.ActiveCount++;
        return pooled.gameObject;
    }

    public bool Return(GameObject instance)
    {
        if (instance == null || !instance.TryGetComponent(out PooledObject pooled) ||
            pooled.Owner != this || !pooled.IsRented ||
            !pools.TryGetValue(pooled.PoolId, out PoolGroup group))
            return false;

        int variant = pooled.VariantIndex;
        if (variant < 0 || variant >= group.Buckets.Length)
            return false;
        PoolBucket bucket = group.Buckets[variant];
        if (bucket == null || pooled.ActiveIndex < 0 || pooled.ActiveIndex >= bucket.Active.Count ||
            bucket.Active[pooled.ActiveIndex] != pooled)
            return false;

        int lastIndex = bucket.Active.Count - 1;
        PooledObject last = bucket.Active[lastIndex];
        bucket.Active[pooled.ActiveIndex] = last;
        last.ActiveIndex = pooled.ActiveIndex;
        bucket.Active.RemoveAt(lastIndex);
        group.ActiveCount--;
        pooled.IsRented = false;
        pooled.ActiveIndex = -1;
        instance.SetActive(false);
        if (instance.transform.parent != bucket.Parent)
            instance.transform.SetParent(bucket.Parent, false);
        bucket.Available.Enqueue(new AvailableObject(pooled));
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
                if (pooled != null && Return(pooled.gameObject))
                    continue;
                bucket.Active.RemoveAt(bucket.Active.Count - 1);
                group.ActiveCount = Mathf.Max(0, group.ActiveCount - 1);
                bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
            }
        }
    }

    private void Shrink(PoolGroup group, float now)
    {
        int destroyed = 0;
        for (int i = 0; i < group.Buckets.Length && destroyed < 2; i++)
        {
            int index = (group.NextShrinkBucket + i) % group.Buckets.Length;
            PoolBucket bucket = group.Buckets[index];
            if (bucket == null)
                continue;
            while (bucket.Available.Count > 0 && bucket.Available.Peek().Object == null)
            {
                bucket.Available.Dequeue();
                bucket.CreatedCount = Mathf.Max(0, bucket.CreatedCount - 1);
            }
            if (bucket.CreatedCount <= bucket.InitialCount || bucket.Available.Count == 0)
                continue;
            AvailableObject oldest = bucket.Available.Peek();
            if (now - oldest.ReturnedAt < group.ShrinkIdleTime)
                continue;
            bucket.Available.Dequeue();
            bucket.CreatedCount--;
            Destroy(oldest.Object.gameObject);
            destroyed++;
        }
        group.NextShrinkBucket = (group.NextShrinkBucket + 1) % group.Buckets.Length;
    }

    public int GetActiveCount(PoolId id)
    {
        return pools.TryGetValue(id, out PoolGroup group) ? group.ActiveCount : 0;
    }

    // JSON stores pool balance only. Scene Transform parents and layer references stay intact.
    private void LoadSettings()
    {
        if (settingsLoaded)
            return;

        PoolManagerSettings data = JsonSettingsStore.Load<PoolManagerSettings>(
            settingsJson, "Config/Settings/PoolManager");
        shrinkDuringGameplay = data.shrinkDuringGameplay;

        if (groups == null || groups.Length == 0)
        {
            Debug.LogError("[AeroShift] PoolManager has no scene pool groups. Keep the original scene references.", this);
            settingsLoaded = true;
            return;
        }

        for (int i = 0; i < data.groups.Length; i++)
        {
            PoolGroupSettings source = data.groups[i];
            PoolGroupConfig target = null;
            for (int j = 0; j < groups.Length; j++)
            {
                if (groups[j] != null && groups[j].id == source.id)
                {
                    target = groups[j];
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning("[AeroShift] Pool group not found in scene: " + source.id, this);
                continue;
            }

            target.resourceInitialCount = source.resourceInitialCount;
            target.resourceExpandWhenEmpty = source.resourceExpandWhenEmpty;
            target.initializeOnFirstUse = source.initializeOnFirstUse;
            target.enableAutoShrink = source.enableAutoShrink;
            target.idleSecondsBeforeShrink = source.idleSecondsBeforeShrink;
            target.shrinkCheckIntervalSeconds = source.shrinkCheckIntervalSeconds;
        }
        settingsLoaded = true;
    }

}
