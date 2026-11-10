using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapManager : MonoBehaviour
{
    private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("AeroShift.MapStreaming.Update");
    private static readonly ProfilerMarker SpawnMarker = new ProfilerMarker("AeroShift.MapStreaming.Spawn");
    private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("AeroShift.MapStreaming.Prepare");
    private static readonly ProfilerMarker RecycleMarker = new ProfilerMarker("AeroShift.MapStreaming.Recycle");
    private static readonly ProfilerMarker CollisionCullingMarker = new ProfilerMarker("AeroShift.Physics.ChunkCollisionCulling");

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private PlayerRewind playerRewind;

    [Header("Chunk Layout")]
    [SerializeField, Min(1f)] private float chunkLength = 80f;
    [SerializeField] private Vector2 mapOriginXY = Vector2.zero;
    [SerializeField, Min(1)] private int forwardChunkCount = 6;
    [SerializeField, Min(0)] private int startingSafeChunkCount = 3;
    [SerializeField, Min(0f)] private float keepBehindDistance = 160f;
    [SerializeField, Min(1)] private int maxSpawnsPerFrame = 2;

    [Header("Selective Preparation")]
    [SerializeField, Min(0)] private int prepareLookaheadChunks = 4;
    [SerializeField] private bool enableCachedPreparation = true;
    [SerializeField, Min(0.02f)] private float prepareRetryInterval = 0.25f;

    [Header("Dimension Transition")]
    [SerializeField, Min(1)] private int transitionSafeChunkCount = 3;
    [SerializeField] private bool spawnTransitionWall = true;
    [SerializeField, Min(0f)] private float transitionWallOffset = 30f;

    [Header("Obstacle Collision Culling")]
    [SerializeField] private bool enableProximityCollisionCulling;
    [SerializeField] private LayerMask obstacleCollisionLayers = 1;
    [SerializeField, Min(0f)] private float collisionEnableAhead = 160f;
    [SerializeField, Min(0f)] private float collisionKeepBehind = 80f;
    [SerializeField, Min(0.02f)] private float collisionCheckInterval = 0.1f;

    [Header("Deterministic Layout")]
    [SerializeField] private int seed = 20261027;
    [SerializeField] private bool allow180DegreeRotation;

    private readonly struct ActiveChunk
    {
        public readonly GameObject Instance;
        public readonly float StartZ;
        public readonly float EndZ;
        public readonly MapChunk Chunk;
        public ProximityCollisionGroup CollisionGroup => Chunk != null ? Chunk.CollisionGroup : null;

        public ActiveChunk(GameObject instance, float startZ, float endZ, MapChunk chunk)
        {
            Instance = instance;
            StartZ = startZ;
            EndZ = endZ;
            Chunk = chunk;
        }
    }

    private readonly Queue<ActiveChunk> activeChunks = new Queue<ActiveChunk>(16);
    private string[] normalPrefabNames = Array.Empty<string>();
    private string[] safePrefabNames = Array.Empty<string>();
    private int[] normalVariantIndices = Array.Empty<int>();
    private int[] safeVariantIndices = Array.Empty<int>();
    private int[] normalPrepareDemand = Array.Empty<int>();
    private int[] safePrepareDemand = Array.Empty<int>();
    private PoolId[] prepareOrderIds = Array.Empty<PoolId>();
    private int[] prepareOrderVariants = Array.Empty<int>();
    private int prepareOrderCount;
    private int initialChunkIndex;
    private int nextChunkIndex;
    private int pendingSafeChunks;
    private bool initialized;
    private bool poolExhaustionWarned;
    private bool is3DMapActive = true;
    private bool transitionRequested;
    private int transitionFirstChunkIndex = -1;
    private int transitionLastChunkIndex = -1;
    private int transitionSafeChunksPlaced;
    private GameObject transitionWall;
    private bool transitionWallWarned;
    private float collisionCheckTimer;
    private bool wasCollisionCullingEnabled;
    private bool lastGameplayCollisionAllowed;
    private int lastCollisionLayerMask;
    private Transform collisionBoundsPlayer;
    private Bounds playerLocalCollisionBounds;
    private bool playerCollisionBoundsValid;
    private bool prepareCacheValid;
    private int cachedNextChunkIndex;
    private int cachedPendingSafeChunks;
    private int cachedTransitionLastChunkIndex;
    private int cachedSeed;
    private int cachedLookaheadChunks;
    private bool cachedTransitionRequested;
    private int cachedStartingSafeChunkCount;
    private uint cachedNormalAvailabilityVersion;
    private uint cachedSafeAvailabilityVersion;
    private bool preparationSatisfied;
    private float nextPrepareAttemptTime;

    public int CollisionCullingStateChanges { get; private set; }
    public int PrepareDemandRebuildCount { get; private set; }
    public int PreparePassCount { get; private set; }

    private float LogicalPlayerZ => player == null ? 0f :
        player.position.z - (worldMovement != null ? worldMovement.transform.position.z : 0f);

    private Vector3 WorldOrigin => worldMovement != null ? worldMovement.transform.position : Vector3.zero;

    public int CurrentSeed => seed;
    public int NextChunkIndex => nextChunkIndex;
    public int ActiveChunkCount => activeChunks.Count;
    public int TotalChunksSpawned { get; private set; }
    public int TotalChunksRecycled { get; private set; }
    public int TotalPrepareCreations { get; private set; }
    public int TotalFailedSpawnAttempts { get; private set; }
    public float ChunkLength => chunkLength;
    // 논리 인덱스가 충분해도 맵이 숨겨졌거나 모두 반환된 상태라면
    // 실제 3D 스트림의 커버리지가 충족된 것으로 기록하지 않습니다.
    public bool HasRequiredCoverage => initialized && is3DMapActive &&
        player != null && activeChunks.Count > 0 &&
        ((double)nextChunkIndex - 0.5d) * chunkLength >=
        (double)LogicalPlayerZ + forwardChunkCount * chunkLength;

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
            Debug.LogError("MapManager에 Player, PoolManager, ResourceManager가 필요합니다.", this);
            enabled = false;
            return;
        }

        if (!LoadVariantCatalog())
        {
            enabled = false;
            return;
        }

        InitializeAtPlayerPosition();
    }

    private bool LoadVariantCatalog()
    {
        if (!TryReadGroup(PoolId.NormalMap, out normalPrefabNames, out normalVariantIndices))
        {
            Debug.LogError("NormalMap 그룹에 유효한 맵 프리팹이 필요합니다.", this);
            return false;
        }

        normalPrepareDemand = new int[normalPrefabNames.Length];
        GameObject[] safePrefabs = resourceManager.LoadPrefabGroup(PoolId.SafeMap);

        if (safePrefabs.Length > 0)
        {
            if (!TryReadGroup(PoolId.SafeMap, out safePrefabNames, out safeVariantIndices))
            {
                Debug.LogError("SafeMap 프리팹 목록이 PoolManager 설정과 일치하지 않습니다.", this);
                return false;
            }

            safePrepareDemand = new int[safePrefabNames.Length];
        }

        if (startingSafeChunkCount > 0 && safePrefabNames.Length == 0)
        {
            Debug.LogError("초기 안전 맵이 설정됐지만 SafeMap 프리팹이 없습니다.", this);
            return false;
        }

        int variantCount = normalPrefabNames.Length + safePrefabNames.Length;
        prepareOrderIds = new PoolId[variantCount];
        prepareOrderVariants = new int[variantCount];
        return true;
    }

    private bool TryReadGroup(PoolId id, out string[] prefabNames, out int[] variantIndices)
    {
        prefabNames = Array.Empty<string>();
        variantIndices = Array.Empty<int>();

        GameObject[] prefabs = resourceManager.LoadPrefabGroup(id);

        if (prefabs.Length == 0 || poolManager.GetVariantCount(id) != prefabs.Length)
            return false;

        string[] names = new string[prefabs.Length];
        int[] indices = new int[prefabs.Length];
        HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];

            if (prefab == null || !usedNames.Add(prefab.name) ||
                !poolManager.TryGetVariantIndex(id, prefab.name, out int variantIndex))
            {
                Debug.LogError($"{id} 그룹의 프리팹 이름이 중복되었거나 PoolManager에 등록되지 않았습니다.", this);
                return false;
            }

            names[i] = prefab.name;
            indices[i] = variantIndex;
        }

        prefabNames = names;
        variantIndices = indices;
        return true;
    }

    private void Update()
    {
        if (!initialized || !is3DMapActive || (playerRewind != null && playerRewind.IsRewinding))
            return;

        using (UpdateMarker.Auto())
        {
            RecycleOldChunks();
            SpawnAhead(maxSpawnsPerFrame);
            PrepareUpcoming();
            TrySpawnTransitionWall();
        }
    }

    private void FixedUpdate()
    {
        if (!initialized || !is3DMapActive)
            return;

        if (!enableProximityCollisionCulling)
        {
            if (wasCollisionCullingEnabled)
            {
                RestoreAllObstacleColliders();
                wasCollisionCullingEnabled = false;
            }

            return;
        }

        bool collisionAllowed = IsGameplayCollisionAllowed();
        collisionCheckTimer += Time.fixedDeltaTime;

        if (wasCollisionCullingEnabled &&
            collisionAllowed == lastGameplayCollisionAllowed &&
            lastCollisionLayerMask == obstacleCollisionLayers.value &&
            collisionCheckTimer < collisionCheckInterval)
            return;

        wasCollisionCullingEnabled = true;
        lastGameplayCollisionAllowed = collisionAllowed;
        lastCollisionLayerMask = obstacleCollisionLayers.value;
        collisionCheckTimer = 0f;

        using (CollisionCullingMarker.Auto())
        {
            bool hasWindow = TryGetCollisionWindow(out float minZ, out float maxZ);

            foreach (ActiveChunk chunk in activeChunks)
            {
                if (chunk.Chunk == null || chunk.Chunk.IsSafe)
                    continue;

                ProximityCollisionGroup group = chunk.Chunk.GetCollisionGroup(obstacleCollisionLayers);
                bool enable = collisionAllowed && IsChunkInCollisionRange(
                    group, hasWindow, minZ, maxZ);

                if (group.SetCollisionsEnabled(enable))
                    CollisionCullingStateChanges++;
            }
        }
    }

    private bool IsGameplayCollisionAllowed()
    {
        return GameManager.Instance != null &&
            GameManager.Instance.CurrentState == GameStates.Playing &&
            (playerRewind == null || !playerRewind.IsRewinding);
    }

    private bool TryGetCollisionWindow(out float minZ, out float maxZ)
    {
        minZ = maxZ = 0f;

        if (player == null)
            return false;

        if (collisionBoundsPlayer != player)
        {
            collisionBoundsPlayer = player;
            Collider[] colliders = player.GetComponentsInChildren<Collider>(true);
            playerCollisionBoundsValid = colliders.Length > 0;
            bool hasBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (!ProximityCollisionGroup.TryGetLocalBounds(player, colliders[i], out Bounds bounds))
                {
                    playerCollisionBoundsValid = false;
                    break;
                }

                if (hasBounds)
                    playerLocalCollisionBounds.Encapsulate(bounds);
                else
                    playerLocalCollisionBounds = bounds;

                hasBounds = true;
            }
        }

        if (!playerCollisionBoundsValid)
            return false;

        ProximityCollisionGroup.GetWorldZRange(player, playerLocalCollisionBounds, out minZ, out maxZ);
        float speed = worldMovement != null ? Mathf.Abs(worldMovement.CurrentSpeed) : 0f;
        // Include movement until the next check, one long render frame and a physics step.
        float travelPadding = speed * (collisionCheckInterval + Time.maximumDeltaTime + Time.fixedDeltaTime);
        minZ -= collisionKeepBehind + travelPadding + Physics.defaultContactOffset;
        maxZ += collisionEnableAhead + travelPadding + Physics.defaultContactOffset;
        return true;
    }

    private static bool IsChunkInCollisionRange(ProximityCollisionGroup group, bool hasWindow, float minZ, float maxZ)
    {
        return !hasWindow || !group.TryGetWorldZRange(out float groupMinZ, out float groupMaxZ) ||
            (groupMaxZ >= minZ && groupMinZ <= maxZ);
    }

    private void RestoreAllObstacleColliders()
    {
        foreach (ActiveChunk chunk in activeChunks)
        {
            if (chunk.CollisionGroup != null && chunk.CollisionGroup.SetCollisionsEnabled(true))
                CollisionCullingStateChanges++;
        }
    }

    private void InitializeAtPlayerPosition()
    {
        // Map prefab floors are centered on the chunk root (-length/2 to +length/2).
        // Select the chunk containing the player, including after 2D -> 3D ResetMaps.
        initialChunkIndex = Mathf.FloorToInt(LogicalPlayerZ / chunkLength + 0.5f);
        nextChunkIndex = initialChunkIndex;
        pendingSafeChunks = 0;
        poolExhaustionWarned = false;
        is3DMapActive = true;
        initialized = true;
        collisionCheckTimer = collisionCheckInterval;
        wasCollisionCullingEnabled = false;
        InvalidatePrepareCache();
        SpawnAhead(maxSpawnsPerFrame);
        PrepareUpcoming();
    }

    private void RecycleOldChunks()
    {
        using (RecycleMarker.Auto())
        {
            float releaseBeforeZ = LogicalPlayerZ - keepBehindDistance;

            // A rewind must not reach a chunk that has already been returned.
            if (playerRewind != null && playerRewind.TryGetOldestLogicalZ(out float rewindZ))
                releaseBeforeZ = Mathf.Min(releaseBeforeZ, rewindZ - chunkLength);

            while (activeChunks.Count > 0 && activeChunks.Peek().EndZ < releaseBeforeZ)
            {
                ActiveChunk chunk = activeChunks.Dequeue();

                if (chunk.Instance != null)
                {
                    poolManager.Return(chunk.Instance);
                }

                TotalChunksRecycled++;
            }
        }
    }

    private void SpawnAhead(int maxCount)
    {
        if (transitionRequested && nextChunkIndex > transitionLastChunkIndex)
            return;

        double desiredEndZ = (double)LogicalPlayerZ + forwardChunkCount * chunkLength;
        int spawnedThisCall = 0;

        // nextChunkIndex is the CENTER, not the leading edge of the next prefab.
        while (((double)nextChunkIndex - 0.5d) * chunkLength < desiredEndZ &&
               spawnedThisCall < maxCount)
        {
            if (transitionRequested && nextChunkIndex > transitionLastChunkIndex)
                break;

            if (!TrySpawnNextChunk())
            {
                TotalFailedSpawnAttempts++;

                if (!poolExhaustionWarned)
                {
                    Debug.LogWarning("MapManager: 필요한 맵이 아직 풀에 없거나 생성 한도에 도달했습니다. 다음 프레임에 재시도합니다.", this);
                    poolExhaustionWarned = true;
                }

                break;
            }

            poolExhaustionWarned = false;
            spawnedThisCall++;
        }
    }

    private bool TrySpawnNextChunk()
    {
        using (SpawnMarker.Auto())
        {
            bool initialSafe = IsInitialSafe(nextChunkIndex);
            bool safe = initialSafe || pendingSafeChunks > 0;
            PoolId id = safe ? PoolId.SafeMap : PoolId.NormalMap;
            int prefabIndex = SelectPrefabIndex(nextChunkIndex, safe);
            int[] variants = safe ? safeVariantIndices : normalVariantIndices;

            if (prefabIndex < 0 || prefabIndex >= variants.Length)
                return false;

            bool rotated = !safe && allow180DegreeRotation &&
                ChunkSeedUtility.ShouldRotate180(seed, nextChunkIndex);
            // All current road/safe-road prefab roots are at the FLOOR CENTER.
            // Rotating around that center must NOT translate a map by one chunk.
            float centerZ = nextChunkIndex * chunkLength;
            float startZ = centerZ - chunkLength * 0.5f;
            float endZ = centerZ + chunkLength * 0.5f;
            Vector3 position = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, centerZ);
            Quaternion rotation = rotated ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            GameObject instance = poolManager.RentInactive(id, position, rotation,
                worldMovement != null ? worldMovement.transform : null, variants[prefabIndex]);

            if (instance == null)
            {
                InvalidatePrepareCache();
                return false;
            }

            if (!instance.TryGetComponent(out MapChunk mapChunk))
                mapChunk = instance.AddComponent<MapChunk>();

            mapChunk.SetChunkData(nextChunkIndex, startZ, chunkLength, safe, rotated);

            if (!safe && enableProximityCollisionCulling)
            {
                ProximityCollisionGroup collisionGroup = mapChunk.GetCollisionGroup(obstacleCollisionLayers);
                bool hasWindow = TryGetCollisionWindow(out float minZ, out float maxZ);
                bool enableCollisions = IsGameplayCollisionAllowed() &&
                    IsChunkInCollisionRange(collisionGroup, hasWindow, minZ, maxZ);

                if (collisionGroup.SetCollisionsEnabled(enableCollisions))
                    CollisionCullingStateChanges++;

                // OFF must restore these groups even before the first FixedUpdate.
                wasCollisionCullingEnabled = true;
            }

            activeChunks.Enqueue(new ActiveChunk(instance, startZ, endZ, mapChunk));
            instance.SetActive(true);

            if (transitionRequested &&
                nextChunkIndex >= transitionFirstChunkIndex &&
                nextChunkIndex <= transitionLastChunkIndex)
            {
                transitionSafeChunksPlaced++;
            }

            nextChunkIndex++;
            TotalChunksSpawned++;

            if (!initialSafe && pendingSafeChunks > 0)
                pendingSafeChunks--;

            return true;
        }
    }

    private void PrepareUpcoming()
    {
        if (prepareLookaheadChunks <= 0 ||
            (transitionRequested && nextChunkIndex > transitionLastChunkIndex))
            return;

        bool windowChanged = !prepareCacheValid ||
            cachedNextChunkIndex != nextChunkIndex ||
            cachedPendingSafeChunks != pendingSafeChunks ||
            cachedTransitionLastChunkIndex != transitionLastChunkIndex ||
            cachedTransitionRequested != transitionRequested ||
            cachedSeed != seed ||
            cachedStartingSafeChunkCount != startingSafeChunkCount ||
            cachedLookaheadChunks != prepareLookaheadChunks;

        if (enableCachedPreparation && !windowChanged)
        {
            bool availabilityUnchanged =
                cachedNormalAvailabilityVersion == poolManager.GetAvailabilityVersion(PoolId.NormalMap) &&
                cachedSafeAvailabilityVersion == poolManager.GetAvailabilityVersion(PoolId.SafeMap);

            if (availabilityUnchanged && (preparationSatisfied || Time.unscaledTime < nextPrepareAttemptTime))
                return;
        }

        using (PrepareMarker.Auto())
        {
            if (windowChanged || !enableCachedPreparation)
                RebuildPrepareDemands();

            int createdThisPass = 0;

            for (int i = 0; i < prepareOrderCount; i++)
                createdThisPass += PrepareVariant(prepareOrderIds[i], prepareOrderVariants[i], 1);

            preparationSatisfied = true;

            for (int i = 0; i < prepareOrderCount; i++)
            {
                PoolId id = prepareOrderIds[i];
                int variantIndex = prepareOrderVariants[i];
                int desired = id == PoolId.SafeMap
                    ? safePrepareDemand[variantIndex]
                    : normalPrepareDemand[variantIndex];

                if (desired > 1)
                    createdThisPass += PrepareVariant(id, variantIndex, desired);

                int poolVariant = id == PoolId.SafeMap
                    ? safeVariantIndices[variantIndex] : normalVariantIndices[variantIndex];
                if (poolManager.GetAvailableCount(id, poolVariant) < desired)
                    preparationSatisfied = false;
            }

            TotalPrepareCreations += createdThisPass;
            PreparePassCount++;
            cachedNormalAvailabilityVersion = poolManager.GetAvailabilityVersion(PoolId.NormalMap);
            cachedSafeAvailabilityVersion = poolManager.GetAvailabilityVersion(PoolId.SafeMap);
            nextPrepareAttemptTime = Time.unscaledTime +
                (createdThisPass > 0 ? 0f : prepareRetryInterval);
        }

        if (!enableCachedPreparation)
            prepareCacheValid = false;
    }

    private int PrepareVariant(PoolId id, int variantIndex, int desiredAvailableCount)
    {
        int poolVariant = id == PoolId.SafeMap
            ? safeVariantIndices[variantIndex]
            : normalVariantIndices[variantIndex];

        return poolManager.Prepare(id, poolVariant, desiredAvailableCount);
    }

    private void RebuildPrepareDemands()
    {
        Array.Clear(normalPrepareDemand, 0, normalPrepareDemand.Length);
        Array.Clear(safePrepareDemand, 0, safePrepareDemand.Length);
        prepareOrderCount = 0;

        int remainingSafe = pendingSafeChunks;

        for (int offset = 0; offset < prepareLookaheadChunks; offset++)
        {
            int chunkIndex = nextChunkIndex + offset;

            if (transitionRequested && chunkIndex > transitionLastChunkIndex)
                break;

            bool initialSafe = IsInitialSafe(chunkIndex);
            bool safe = initialSafe || remainingSafe > 0;

            if (!initialSafe && remainingSafe > 0)
                remainingSafe--;

            int prefabIndex = SelectPrefabIndex(chunkIndex, safe);

            if (prefabIndex < 0)
                continue;

            int previousDemand = safe
                ? safePrepareDemand[prefabIndex]++
                : normalPrepareDemand[prefabIndex]++;

            if (previousDemand == 0)
            {
                prepareOrderIds[prepareOrderCount] = safe ? PoolId.SafeMap : PoolId.NormalMap;
                prepareOrderVariants[prepareOrderCount] = prefabIndex;
                prepareOrderCount++;
            }
        }

        cachedNextChunkIndex = nextChunkIndex;
        cachedPendingSafeChunks = pendingSafeChunks;
        cachedTransitionLastChunkIndex = transitionLastChunkIndex;
        cachedTransitionRequested = transitionRequested;
        cachedSeed = seed;
        cachedLookaheadChunks = prepareLookaheadChunks;
        cachedStartingSafeChunkCount = startingSafeChunkCount;
        prepareCacheValid = true;
        PrepareDemandRebuildCount++;
    }

    private void InvalidatePrepareCache()
    {
        prepareCacheValid = false;
        preparationSatisfied = false;
        nextPrepareAttemptTime = 0f;
    }

    private bool IsInitialSafe(int chunkIndex)
    {
        return (long)chunkIndex < (long)initialChunkIndex + startingSafeChunkCount;
    }

    private int SelectPrefabIndex(int chunkIndex, bool safe)
    {
        int count = safe ? safePrefabNames.Length : normalPrefabNames.Length;

        if (count == 0)
            return -1;

        return safe ? 0 : ChunkSeedUtility.SelectNormalPrefab(seed, chunkIndex, count);
    }

    public int RequestSafeChunks(int count)
    {
        if (!initialized || count <= 0 || safePrefabNames.Length == 0)
            return -1;

        int firstScheduledIndex = nextChunkIndex;
        pendingSafeChunks = count > int.MaxValue - pendingSafeChunks
            ? int.MaxValue
            : pendingSafeChunks + count;
        PrepareUpcoming();
        return firstScheduledIndex;
    }

    public void ResetStream(int newSeed)
    {
        if (!initialized || player == null || poolManager == null)
            return;

        ClearTransitionState();
        ClearActiveChunks();
        seed = newSeed;
        TotalChunksSpawned = 0;
        TotalChunksRecycled = 0;
        TotalPrepareCreations = 0;
        TotalFailedSpawnAttempts = 0;
        PrepareDemandRebuildCount = 0;
        PreparePassCount = 0;
        InitializeAtPlayerPosition();
    }

    public void SpawnTransitionSafeMaps()
    {
        if (!initialized || !is3DMapActive || transitionRequested)
            return;

        if (safePrefabNames.Length == 0)
        {
            Debug.LogError("차원 전환용 SafeMap 프리팹이 등록되지 않았습니다.", this);
            return;
        }

        int safeCount = Mathf.Max(1, transitionSafeChunkCount);

        if (nextChunkIndex > int.MaxValue - safeCount)
        {
            Debug.LogError("차원 전환 맵 인덱스 범위를 초과했습니다.", this);
            return;
        }

        int firstIndex = RequestSafeChunks(safeCount);

        if (firstIndex < 0)
            return;

        transitionRequested = true;
        transitionFirstChunkIndex = firstIndex;
        transitionLastChunkIndex = firstIndex + safeCount - 1;
        transitionSafeChunksPlaced = 0;
        transitionWallWarned = false;
    }

    public bool IsTransitionMapArrived(float _)
    {
        if (!transitionRequested || player == null)
            return false;

        int requiredCount = transitionLastChunkIndex - transitionFirstChunkIndex + 1;

        return transitionSafeChunksPlaced >= requiredCount &&
            (double)LogicalPlayerZ >= ((double)transitionFirstChunkIndex - 0.5d) * chunkLength;
    }

    public void HideMaps()
    {
        if (!initialized)
            return;

        is3DMapActive = false;
        wasCollisionCullingEnabled = false;
        ClearActiveChunks();
        ReturnTransitionWall();
    }

    public void EndTransitionMap()
    {
        ClearTransitionState();
    }

    public void ResetMaps()
    {
        ResetStream(seed);
    }

    private void TrySpawnTransitionWall()
    {
        if (!transitionRequested || !is3DMapActive ||
            !spawnTransitionWall || transitionWall != null || poolManager == null)
            return;

        int requiredCount = transitionLastChunkIndex - transitionFirstChunkIndex + 1;

        if (transitionSafeChunksPlaced < requiredCount)
            return;

        // Offset is measured from the actual forward edge of the last centered safe map.
        float wallZ = ((float)transitionLastChunkIndex + 0.5f) * chunkLength + transitionWallOffset;
        Vector3 wallPosition = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, wallZ);
        transitionWall = poolManager.RentInactive(PoolId.TransitionWall, wallPosition, Quaternion.identity,
            worldMovement != null ? worldMovement.transform : null);

        if (transitionWall != null)
            transitionWall.SetActive(true);

        if (transitionWall == null && !transitionWallWarned)
        {
            Debug.LogWarning("TransitionWall 풀에서 벽을 가져오지 못했습니다. 벽 없이 차원 전환을 진행합니다.", this);
            transitionWallWarned = true;
        }
    }

    private void ReturnTransitionWall()
    {
        if (transitionWall == null)
            return;

        if (poolManager != null)
            poolManager.Return(transitionWall);

        transitionWall = null;
    }

    private void ClearTransitionState()
    {
        ReturnTransitionWall();
        transitionRequested = false;
        transitionFirstChunkIndex = -1;
        transitionLastChunkIndex = -1;
        transitionSafeChunksPlaced = 0;
        transitionWallWarned = false;
        pendingSafeChunks = 0;
    }

    private void ClearActiveChunks()
    {
        while (activeChunks.Count > 0)
        {
            ActiveChunk chunk = activeChunks.Dequeue();

            if (chunk.Instance == null || poolManager == null)
                continue;

            poolManager.Return(chunk.Instance);
        }
    }

    [ContextMenu("Log Streaming Stats")]
    private void LogStreamingStats()
    {
        int activeNormal = poolManager != null ? poolManager.GetActiveCount(PoolId.NormalMap) : 0;
        int activeSafe = poolManager != null ? poolManager.GetActiveCount(PoolId.SafeMap) : 0;
        Debug.Log($"MapStreaming | Seed={seed}, Next={nextChunkIndex}, Active={activeChunks.Count}, CoverageReady={HasRequiredCoverage}, Spawned={TotalChunksSpawned}, Recycled={TotalChunksRecycled}, PrepareCreated={TotalPrepareCreations}, PrepareRebuilds={PrepareDemandRebuildCount}, PreparePasses={PreparePassCount}, FailedAttempts={TotalFailedSpawnAttempts}, NormalActive={activeNormal}, SafeActive={activeSafe}, CollisionStateChanges={CollisionCullingStateChanges}", this);
    }

    private void OnDisable()
    {
        RestoreAllObstacleColliders();
        wasCollisionCullingEnabled = false;
    }

    private void OnDestroy()
    {
        ReturnTransitionWall();
        ClearActiveChunks();
    }

    private void OnValidate()
    {
        chunkLength = Mathf.Max(1f, chunkLength);
        forwardChunkCount = Mathf.Max(1, forwardChunkCount);
        startingSafeChunkCount = Mathf.Max(0, startingSafeChunkCount);
        keepBehindDistance = Mathf.Max(0f, keepBehindDistance);
        maxSpawnsPerFrame = Mathf.Max(1, maxSpawnsPerFrame);
        prepareLookaheadChunks = Mathf.Max(0, prepareLookaheadChunks);
        prepareRetryInterval = Mathf.Max(0.02f, prepareRetryInterval);
        transitionSafeChunkCount = Mathf.Max(1, transitionSafeChunkCount);
        transitionWallOffset = Mathf.Max(0f, transitionWallOffset);
        collisionEnableAhead = Mathf.Max(0f, collisionEnableAhead);
        collisionKeepBehind = Mathf.Max(0f, collisionKeepBehind);
        collisionCheckInterval = Mathf.Max(0.02f, collisionCheckInterval);
    }
}
