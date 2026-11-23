using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class MapManager : MonoBehaviour
{
    [SerializeField] private TextAsset settingsJson;
    [SerializeField] private TextAsset generationSettingsJson;
    private MapGenerationSettingsData generationSettings;
    private bool settingsLoaded;

    [SerializeField] private Transform player;
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private PlayerRewind playerRewind;

    private float chunkLength;
    private Vector2 mapOriginXY;
    private int forwardChunkCount;
    private int startingSafeChunkCount;
    private float keepBehindDistance;
    private int maxSpawnsPerFrame;
    private int prepareLookaheadChunks;

    private int transitionSafeChunkCount;
    private bool spawnTransitionWall;
    private float transitionWallOffset;

    private bool enableProximityCollisionCulling;
    private LayerMask obstacleCollisionLayers;
    private float collisionEnableAhead;
    private float collisionKeepBehind;
    private float collisionCheckInterval;

    private int seed;
    private bool allow180DegreeRotation;

    public GameMode CurrentGameMode { get; private set; } = GameMode.Regular;

    public void SetGameMode(GameMode mode)
    {
        if (CurrentGameMode == mode || GameManager.IsPlaying)
            return;

        if (mode == GameMode.Random && !TryLoadGenerationSettings())
            return;

        CurrentGameMode = mode;

        if (initialized)
            ResetMaps();
    }

    private struct ActiveChunk
    {
        public GameObject Object;
        public MapChunk Chunk;
        public float EndZ;

        public ActiveChunk(GameObject instance, MapChunk chunk, float endZ)
        {
            Object = instance;
            Chunk = chunk;
            EndZ = endZ;
        }
    }

    private readonly Queue<ActiveChunk> activeChunks = new Queue<ActiveChunk>();
    private int[] normalVariants = Array.Empty<int>();
    private int[] safeVariants = Array.Empty<int>();
    private int[] normalDemand = Array.Empty<int>();
    private MapLayoutDefinition[] normalLayouts = Array.Empty<MapLayoutDefinition>();
    private int[][] layoutStructureVariants = Array.Empty<int[]>();
    private int[] structureVariants = Array.Empty<int>();
    private int[] structureDemand = Array.Empty<int>();
    private int[] structureRequired = Array.Empty<int>();
    private int lastPreparedChunk = int.MinValue;
    private float nextPrepareTime;
    private int[] safeDemand = Array.Empty<int>();
    private Collider[] playerColliders;

    private int initialChunkIndex;
    private int nextChunkIndex;
    private int pendingSafeChunks;
    private int transitionFirstChunkIndex;
    private int transitionLastChunkIndex;
    private int transitionSafeChunksPlaced;
    private bool transitionRequested;
    private bool initialized;
    private bool is3DMapActive = true;
    private bool collisionWasEnabled;
    private float collisionTimer;
    private GameObject transitionWall;

    private float LogicalPlayerZ
    {
        get
        {
            if (player == null)
                return 0f;
            return player.position.z - (worldMovement != null ? worldMovement.transform.position.z : 0f);
        }
    }

    private Vector3 WorldOrigin
    {
        get { return worldMovement != null ? worldMovement.transform.position : Vector3.zero; }
    }

    private void Start()
    {
        if (poolManager == null)
            poolManager = FindFirstObjectByType<PoolManager>();
        if (resourceManager == null)
            resourceManager = FindFirstObjectByType<ResourceManager>();
        if (worldMovement == null)
            worldMovement = FindFirstObjectByType<WorldMovement>();
        if (player == null || poolManager == null || resourceManager == null)
        {
            enabled = false;
            return;
        }

        normalVariants = LoadVariants(PoolId.NormalMap);
        safeVariants = LoadVariants(PoolId.SafeMap);
        if (normalVariants.Length != 1 || safeVariants.Length == 0 || !LoadMapLayouts())
        {
            Debug.LogError("[AeroShift Map] MapBase, SafeMap, or JSON map layouts are not configured.", this);
            enabled = false;
            return;
        }

        normalDemand = new int[normalVariants.Length];
        safeDemand = new int[safeVariants.Length];
        playerColliders = player.GetComponentsInChildren<Collider>(true);
        seed = GenerateSeed();
        InitializeStream();
    }

    private int[] LoadVariants(PoolId id)
    {
        GameObject[] prefabs = resourceManager.LoadPrefabGroup(id);
        if (prefabs == null || prefabs.Length == 0 || poolManager.GetVariantCount(id) != prefabs.Length)
            return Array.Empty<int>();

        int[] result = new int[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null || !poolManager.TryGetVariantIndex(id, prefabs[i].name, out result[i]))
                return Array.Empty<int>();
        }
        return result;
    }

    private bool LoadMapLayouts()
    {
        const string path = "Config/Maps/MapLayouts";
        TextAsset json = Resources.Load<TextAsset>(path);
        if (json == null)
        {
            Debug.LogError("[AeroShift Map] Missing JSON at Resources/" + path + ".json", this);
            return false;
        }

        MapLayoutDatabase database;
        try
        {
            database = JsonUtility.FromJson<MapLayoutDatabase>(json.text);
        }
        catch (ArgumentException exception)
        {
            Debug.LogError("[AeroShift Map] Invalid layout JSON: " + exception.Message, this);
            return false;
        }

        if (database == null || database.schemaVersion != 1 ||
            database.maps == null || database.maps.Length == 0)
            return false;

        int variantCount = poolManager.GetVariantCount(PoolId.MapStructure);
        if (variantCount == 0)
        {
            Debug.LogError("[AeroShift Map] MapStructure pool has no prefabs.", this);
            return false;
        }

        var knownLayouts = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new int[database.maps.Length][];

        for (int m = 0; m < database.maps.Length; m++)
        {
            MapLayoutDefinition map = database.maps[m];
            if (map == null || string.IsNullOrEmpty(map.id) ||
                !knownLayouts.Add(map.id) || map.structures == null)
                return false;

            resolved[m] = new int[map.structures.Length];
            for (int i = 0; i < map.structures.Length; i++)
            {
                MapStructurePlacement structure = map.structures[i];
                if (structure == null || !IsFinite(structure.position) ||
                    !IsFinite(structure.rotation) || !IsFinite(structure.scale) ||
                    structure.scale.x == 0f || structure.scale.y == 0f || structure.scale.z == 0f ||
                    !poolManager.TryGetVariantIndex(PoolId.MapStructure, structure.prefabId, out int variant))
                {
                    Debug.LogError("[AeroShift Map] Invalid structure entry in " + map.id +
                        " at index " + i, this);
                    return false;
                }
                resolved[m][i] = variant;
            }
        }

        normalLayouts = database.maps;
        layoutStructureVariants = resolved;
        structureVariants = new int[variantCount];
        structureDemand = new int[variantCount];
        structureRequired = new int[variantCount];
        for (int i = 0; i < variantCount; i++)
            structureVariants[i] = i;
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }

    private bool EnsureStructureAvailability(int layoutIndex)
    {
        Array.Clear(structureRequired, 0, structureRequired.Length);
        int[] variants = layoutStructureVariants[layoutIndex];
        for (int i = 0; i < variants.Length; i++)
            structureRequired[variants[i]]++;

        bool ready = true;
        for (int variant = 0; variant < structureRequired.Length; variant++)
        {
            int count = structureRequired[variant];
            if (count == 0)
                continue;
            if (poolManager.GetAvailableCount(PoolId.MapStructure, variant) >= count)
                continue;

            poolManager.Prepare(PoolId.MapStructure, variant, count);
            if (poolManager.GetAvailableCount(PoolId.MapStructure, variant) < count)
                ready = false;
        }
        return ready;
    }

    private void Update()
    {
        if (!initialized || !is3DMapActive || (playerRewind != null && playerRewind.IsRewinding))
            return;

        RecycleChunks();
        SpawnAhead();
        PrepareAhead();
        SpawnWall();
    }

    private void InitializeStream()
    {
        initialChunkIndex = Mathf.FloorToInt(LogicalPlayerZ / Mathf.Max(1f, chunkLength) + 0.5f);
        nextChunkIndex = initialChunkIndex;
        pendingSafeChunks = 0;
        is3DMapActive = true;
        initialized = true;
        collisionTimer = collisionCheckInterval;
        collisionWasEnabled = false;
        lastPreparedChunk = int.MinValue;
        nextPrepareTime = 0f;
        SpawnAhead();
        PrepareAhead();
    }

    private void SpawnAhead()
    {
        double endZ = (double)LogicalPlayerZ + forwardChunkCount * chunkLength;
        int count = 0;
        while (((double)nextChunkIndex - 0.5d) * chunkLength < endZ && count < maxSpawnsPerFrame)
        {
            if (transitionRequested && nextChunkIndex > transitionLastChunkIndex)
                break;
            if (!SpawnChunk())
                break;
            count++;
        }
    }

    private bool SpawnChunk()
    {
        bool initialSafe = IsInitialSafe(nextChunkIndex);
        bool safe = initialSafe || pendingSafeChunks > 0;
        int layoutIndex = safe ? -1 :
            ChunkSeedUtility.SelectNormalPrefab(seed, nextChunkIndex, normalLayouts.Length);
        if (!safe && (layoutIndex < 0 || !EnsureStructureAvailability(layoutIndex)))
            return false;

        float centerZ = nextChunkIndex * chunkLength;
        Vector3 position = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, centerZ);
        bool rotated = !safe && allow180DegreeRotation && ChunkSeedUtility.ShouldRotate180(seed, nextChunkIndex);
        Quaternion rotation = rotated ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        PoolId id = safe ? PoolId.SafeMap : PoolId.NormalMap;
        Transform parent = worldMovement != null ? worldMovement.transform : null;
        int baseVariant = safe ? safeVariants[0] : normalVariants[0];
        GameObject instance = poolManager.RentInactive(id, position, rotation, parent, baseVariant);
        if (instance == null)
            return false;

        if (!instance.TryGetComponent(out MapChunk chunk))
            chunk = instance.AddComponent<MapChunk>();
        chunk.SetChunkData(safe);

        if (!safe && !chunk.ApplyLayout(normalLayouts[layoutIndex],
                layoutStructureVariants[layoutIndex], poolManager))
        {
            poolManager.Return(instance);
            return false;
        }

        if (!safe && enableProximityCollisionCulling)
        {
            ProximityCollisionGroup group = chunk.GetCollisionGroup(obstacleCollisionLayers);
            GetCollisionRange(out float minZ, out float maxZ);
            bool canCollide = IsGameplayCollisionAllowed() && InCollisionRange(group, minZ, maxZ);
            group.SetCollisionsEnabled(canCollide);
            collisionWasEnabled = true;
        }
        else if (!safe && chunk.CollisionGroup != null)
        {
            chunk.CollisionGroup.SetCollisionsEnabled(true);
        }

        activeChunks.Enqueue(new ActiveChunk(instance, chunk, centerZ + chunkLength * 0.5f));
        instance.SetActive(true);
        if (transitionRequested && nextChunkIndex >= transitionFirstChunkIndex &&
            nextChunkIndex <= transitionLastChunkIndex)
            transitionSafeChunksPlaced++;
        nextChunkIndex++;
        if (!initialSafe && pendingSafeChunks > 0)
            pendingSafeChunks--;
        return true;
    }

    private void RecycleChunks()
    {
        float releaseZ = LogicalPlayerZ - keepBehindDistance;
        if (playerRewind != null && playerRewind.TryGetOldestLogicalZ(out float rewindZ))
            releaseZ = Mathf.Min(releaseZ, rewindZ - chunkLength);

        while (activeChunks.Count > 0 && activeChunks.Peek().EndZ < releaseZ)
        {
            ActiveChunk chunk = activeChunks.Dequeue();
            if (chunk.Chunk != null)
                chunk.Chunk.ReleaseStructures();
            if (chunk.Object != null)
                poolManager.Return(chunk.Object);
        }
    }

    private void PrepareAhead()
    {
        if (prepareLookaheadChunks <= 0 || (transitionRequested && nextChunkIndex > transitionLastChunkIndex))
            return;
        if (lastPreparedChunk == nextChunkIndex && Time.unscaledTime < nextPrepareTime)
            return;
        lastPreparedChunk = nextChunkIndex;
        nextPrepareTime = Time.unscaledTime + 0.1f;
        Array.Clear(normalDemand, 0, normalDemand.Length);
        Array.Clear(safeDemand, 0, safeDemand.Length);
        Array.Clear(structureDemand, 0, structureDemand.Length);
        int remainingSafe = pendingSafeChunks;

        for (int i = 0; i < prepareLookaheadChunks; i++)
        {
            int index = nextChunkIndex + i;
            if (transitionRequested && index > transitionLastChunkIndex)
                break;
            bool initialSafe = IsInitialSafe(index);
            bool safe = initialSafe || remainingSafe > 0;
            if (!initialSafe && remainingSafe > 0)
                remainingSafe--;

            if (safe)
            {
                safeDemand[0]++;
                continue;
            }

            normalDemand[0]++;
            int layoutIndex = ChunkSeedUtility.SelectNormalPrefab(seed, index, normalLayouts.Length);
            int[] variants = layoutStructureVariants[layoutIndex];
            for (int j = 0; j < variants.Length; j++)
                structureDemand[variants[j]]++;
        }

        PrepareVariants(PoolId.NormalMap, normalVariants, normalDemand);
        PrepareVariants(PoolId.SafeMap, safeVariants, safeDemand);
        PrepareVariants(PoolId.MapStructure, structureVariants, structureDemand);
    }

    private void PrepareVariants(PoolId id, int[] variants, int[] demand)
    {
        for (int i = 0; i < variants.Length; i++)
        {
            if (demand[i] > 0)
                poolManager.Prepare(id, variants[i], demand[i]);
        }
    }

    private bool IsInitialSafe(int index)
    {
        return (long)index < (long)initialChunkIndex + startingSafeChunkCount;
    }

    public int RequestSafeChunks(int count)
    {
        if (!initialized || count <= 0 || safeVariants.Length == 0)
            return -1;
        int first = nextChunkIndex;
        pendingSafeChunks = count > int.MaxValue - pendingSafeChunks ? int.MaxValue : pendingSafeChunks + count;
        lastPreparedChunk = int.MinValue;
        PrepareAhead();
        return first;
    }

    public void SpawnTransitionSafeMaps()
    {
        if (!initialized || !is3DMapActive || transitionRequested || safeVariants.Length == 0)
            return;
        int count = Mathf.Max(1, transitionSafeChunkCount);
        if (nextChunkIndex > int.MaxValue - count)
            return;
        int first = RequestSafeChunks(count);
        if (first < 0)
            return;
        transitionRequested = true;
        transitionFirstChunkIndex = first;
        transitionLastChunkIndex = first + count - 1;
        transitionSafeChunksPlaced = 0;
    }

    public bool IsTransitionMapArrived()
    {
        if (!transitionRequested || player == null)
            return false;
        int count = transitionLastChunkIndex - transitionFirstChunkIndex + 1;
        return transitionSafeChunksPlaced >= count &&
            (double)LogicalPlayerZ >= ((double)transitionFirstChunkIndex - 0.5d) * chunkLength;
    }

    private void SpawnWall()
    {
        if (!transitionRequested || !spawnTransitionWall || transitionWall != null ||
            transitionSafeChunksPlaced < transitionLastChunkIndex - transitionFirstChunkIndex + 1)
            return;
        float z = ((float)transitionLastChunkIndex + 0.5f) * chunkLength + transitionWallOffset;
        Vector3 position = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, z);
        Transform parent = worldMovement != null ? worldMovement.transform : null;
        transitionWall = poolManager.RentInactive(PoolId.TransitionWall, position, Quaternion.identity, parent);
        if (transitionWall != null)
            transitionWall.SetActive(true);
    }

    public void HideMaps()
    {
        if (!initialized)
            return;
        is3DMapActive = false;
        ClearChunks();
        ReturnWall();
        collisionWasEnabled = false;
    }

    public void EndTransitionMap()
    {
        ClearTransition();
    }

    public void ResetMaps()
    {
        ResetStream(GenerateSeed());
    }

    private int GenerateSeed()
    {
        int newSeed = UnityEngine.Random.Range(1, int.MaxValue);
        if (newSeed == seed)
            newSeed = newSeed == int.MaxValue - 1 ? 1 : newSeed + 1;
        return newSeed;
    }

    public void ResetStream(int newSeed)
    {
        if (!initialized)
            return;
        ClearTransition();
        ClearChunks();
        seed = newSeed;
        InitializeStream();
    }

    private void ClearTransition()
    {
        ReturnWall();
        pendingSafeChunks = 0;
        transitionRequested = false;
        transitionFirstChunkIndex = -1;
        transitionLastChunkIndex = -1;
        transitionSafeChunksPlaced = 0;
    }

    private void ReturnWall()
    {
        if (transitionWall == null)
            return;
        if (poolManager != null)
            poolManager.Return(transitionWall);
        transitionWall = null;
    }

    private void ClearChunks()
    {
        while (activeChunks.Count > 0)
        {
            ActiveChunk chunk = activeChunks.Dequeue();
            if (chunk.Chunk != null)
                chunk.Chunk.ReleaseStructures();
            if (chunk.Object != null && poolManager != null)
                poolManager.Return(chunk.Object);
        }
    }

    private void FixedUpdate()
    {
        if (!initialized || !is3DMapActive)
            return;
        if (!enableProximityCollisionCulling)
        {
            if (collisionWasEnabled)
                RestoreCollisions();
            collisionWasEnabled = false;
            return;
        }

        collisionTimer += Time.fixedDeltaTime;
        if (collisionTimer < Mathf.Max(0.02f, collisionCheckInterval))
            return;
        collisionTimer = 0f;
        collisionWasEnabled = true;
        GetCollisionRange(out float minZ, out float maxZ);
        bool allowed = IsGameplayCollisionAllowed();

        foreach (ActiveChunk chunk in activeChunks)
        {
            if (chunk.Chunk == null || chunk.Chunk.IsSafe)
                continue;
            ProximityCollisionGroup group = chunk.Chunk.GetCollisionGroup(obstacleCollisionLayers);
            group.SetCollisionsEnabled(allowed && InCollisionRange(group, minZ, maxZ));
        }
    }

    private bool IsGameplayCollisionAllowed()
    {
        return GameManager.IsPlaying &&
            (playerRewind == null || !playerRewind.IsRewinding);
    }

    private void GetCollisionRange(out float minZ, out float maxZ)
    {
        minZ = player.position.z;
        maxZ = player.position.z;
        if (playerColliders != null)
        {
            for (int i = 0; i < playerColliders.Length; i++)
            {
                Collider collider = playerColliders[i];
                if (collider == null || !collider.enabled)
                    continue;
                Bounds bounds = collider.bounds;
                minZ = Mathf.Min(minZ, bounds.min.z);
                maxZ = Mathf.Max(maxZ, bounds.max.z);
            }
        }
        float speed = worldMovement != null ? Mathf.Abs(worldMovement.CurrentSpeed) : 0f;
        float padding = speed * (collisionCheckInterval + Time.maximumDeltaTime + Time.fixedDeltaTime) +
            Physics.defaultContactOffset;
        minZ -= collisionKeepBehind + padding;
        maxZ += collisionEnableAhead + padding;
    }

    private bool InCollisionRange(ProximityCollisionGroup group, float minZ, float maxZ)
    {
        return group == null || !group.TryGetWorldZRange(out float start, out float end) ||
            (end >= minZ && start <= maxZ);
    }

    private void RestoreCollisions()
    {
        foreach (ActiveChunk chunk in activeChunks)
        {
            if (chunk.Chunk != null && chunk.Chunk.CollisionGroup != null)
                chunk.Chunk.CollisionGroup.SetCollisionsEnabled(true);
        }
    }

    private void OnDisable()
    {
        RestoreCollisions();
        collisionWasEnabled = false;
    }

    private void OnDestroy()
    {
        ReturnWall();
        ClearChunks();
    }

    private void LoadSettings()
    {
        if (settingsLoaded)
            return;

        MapManagerSettings data = JsonSettingsStore.Load<MapManagerSettings>(
            settingsJson, "Config/Settings/MapManager");
        chunkLength = data.chunkLength;
        mapOriginXY = data.mapOriginXY;
        forwardChunkCount = data.forwardChunkCount;
        startingSafeChunkCount = data.startingSafeChunkCount;
        keepBehindDistance = data.keepBehindDistance;
        maxSpawnsPerFrame = data.maxSpawnsPerFrame;
        prepareLookaheadChunks = data.prepareLookaheadChunks;
        transitionSafeChunkCount = data.transitionSafeChunkCount;
        spawnTransitionWall = data.spawnTransitionWall;
        transitionWallOffset = data.transitionWallOffset;
        enableProximityCollisionCulling = data.enableProximityCollisionCulling;
        obstacleCollisionLayers = data.obstacleCollisionLayers;
        collisionEnableAhead = data.collisionEnableAhead;
        collisionKeepBehind = data.collisionKeepBehind;
        collisionCheckInterval = data.collisionCheckInterval;
        allow180DegreeRotation = data.allow180DegreeRotation;
        settingsLoaded = true;
    }

    private bool TryLoadGenerationSettings()
    {
        if (generationSettings != null)
            return true;

        TextAsset json = generationSettingsJson != null
            ? generationSettingsJson
            : Resources.Load<TextAsset>("Config/Maps/MapGenerationSettings");

        if (json == null)
        {
            Debug.LogError("[AeroShift Map] MapGenerationSettings.json not found.", this);
            return false;
        }

        MapGenerationSettingsData data;

        try
        {
            data = JsonUtility.FromJson<MapGenerationSettingsData>(json.text);
        }
        catch (ArgumentException exception)
        {
            Debug.LogError("[AeroShift Map] Invalid generation JSON: " + exception.Message, this);
            return false;
        }

        if (data == null || data.schemaVersion != 1 ||
            data.difficultyIntervalSeconds <= 0f ||
            float.IsNaN(data.difficultyIntervalSeconds) ||
            float.IsInfinity(data.difficultyIntervalSeconds) ||
            data.maxDifficultyLevel != 5 ||
            data.levels == null || data.levels.Length != data.maxDifficultyLevel ||
            data.minScale <= 0f || float.IsNaN(data.minScale) || float.IsInfinity(data.minScale) ||
            data.maxScale < data.minScale || float.IsNaN(data.maxScale) || float.IsInfinity(data.maxScale) ||
            data.maxOverlapRatio < 0f || data.maxOverlapRatio > 1f ||
            float.IsNaN(data.maxOverlapRatio) ||
            data.maxSafeZoneShiftRatio < 0f || data.maxSafeZoneShiftRatio > 1f ||
            float.IsNaN(data.maxSafeZoneShiftRatio) ||
            data.placementAttemptLimit <= 0)
        {
            Debug.LogError("[AeroShift Map] Invalid generation settings.", this);
            return false;
        }

        for (int i = 0; i < data.levels.Length; i++)
        {
            MapGenerationDifficultyData level = data.levels[i];

            if (level == null || level.level != i + 1 ||
                level.safeZoneSizeRatio <= 0f || level.safeZoneSizeRatio > 1f ||
                float.IsNaN(level.safeZoneSizeRatio) ||
                level.minStones < 0 || level.maxStones < level.minStones ||
                level.weights == null || level.weights.Length == 0)
            {
                Debug.LogError("[AeroShift Map] Invalid difficulty level: " + (i + 1), this);
                return false;
            }

            HashSet<string> prefabNames = new HashSet<string>(StringComparer.Ordinal);
            long totalWeight = 0;

            for (int j = 0; j < level.weights.Length; j++)
            {
                MapGenerationStoneWeight weight = level.weights[j];

                if (weight == null || string.IsNullOrEmpty(weight.prefabId) ||
                    weight.weight <= 0 || !prefabNames.Add(weight.prefabId))
                {
                    Debug.LogError("[AeroShift Map] Invalid stone weight at level: " + level.level, this);
                    return false;
                }

                totalWeight += weight.weight;
            }

            if (totalWeight <= 0)
                return false;
        }

        generationSettings = data;
        return true;
    }

    public MapGenerationDifficultyData GetGenerationDifficulty(float survivalSeconds)
    {
        if (CurrentGameMode != GameMode.Random || !TryLoadGenerationSettings())
            return null;

        int index = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Max(0f, survivalSeconds) /
                generationSettings.difficultyIntervalSeconds),
            0,
            generationSettings.levels.Length - 1);

        return generationSettings.levels[index];
    }

    private void Awake()
    {
        LoadSettings();
    }
}
