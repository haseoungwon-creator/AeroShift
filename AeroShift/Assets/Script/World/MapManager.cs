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

    [Header("Dimension Transition")]
    [SerializeField, Min(1)] private int transitionSafeChunkCount = 3;
    [SerializeField] private bool spawnTransitionWall = true;
    [SerializeField, Min(0f)] private float transitionWallOffset = 30f;

    [Header("Deterministic Layout")]
    [SerializeField] private int seed = 20261027;
    [SerializeField] private bool allow180DegreeRotation;

    private readonly struct ActiveChunk
    {
        public readonly GameObject Instance;
        public readonly float EndZ;

        public ActiveChunk(GameObject instance, float endZ)
        {
            Instance = instance;
            EndZ = endZ;
        }
    }

    private readonly Queue<ActiveChunk> activeChunks = new Queue<ActiveChunk>(16);
    private string[] normalPrefabNames = Array.Empty<string>();
    private string[] safePrefabNames = Array.Empty<string>();
    private int[] normalVariantIndices = Array.Empty<int>();
    private int[] safeVariantIndices = Array.Empty<int>();
    private int[] normalPrepareDemand = Array.Empty<int>();
    private int[] safePrepareDemand = Array.Empty<int>();
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
    public bool HasRequiredCoverage => initialized && player != null &&
        (double)nextChunkIndex * chunkLength >= (double)LogicalPlayerZ + forwardChunkCount * chunkLength;

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

    private void InitializeAtPlayerPosition()
    {
        initialChunkIndex = Mathf.FloorToInt(LogicalPlayerZ / chunkLength);
        nextChunkIndex = initialChunkIndex;
        pendingSafeChunks = 0;
        poolExhaustionWarned = false;
        is3DMapActive = true;
        initialized = true;
        SpawnAhead(maxSpawnsPerFrame);
        PrepareUpcoming();
    }

    private void RecycleOldChunks()
    {
        using (RecycleMarker.Auto())
        {
            float releaseBeforeZ = LogicalPlayerZ - keepBehindDistance;

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

        while ((double)nextChunkIndex * chunkLength < desiredEndZ && spawnedThisCall < maxCount)
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
            float startZ = nextChunkIndex * chunkLength;
            float placementZ = rotated ? startZ + chunkLength : startZ;
            Vector3 position = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, placementZ);
            Quaternion rotation = rotated ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            GameObject instance = poolManager.Rent(id, position, rotation, variants[prefabIndex]);

            if (instance == null)
                return false;

            if (worldMovement != null)
                instance.transform.SetParent(worldMovement.transform, true);

            if (!instance.TryGetComponent(out MapChunk mapChunk))
                mapChunk = instance.AddComponent<MapChunk>();

            mapChunk.SetChunkData(nextChunkIndex, startZ, chunkLength, safe, rotated);

            activeChunks.Enqueue(new ActiveChunk(instance, startZ + chunkLength));

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

        using (PrepareMarker.Auto())
        {
            Array.Clear(normalPrepareDemand, 0, normalPrepareDemand.Length);
            Array.Clear(safePrepareDemand, 0, safePrepareDemand.Length);
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

                if (safe)
                {
                    int desired = ++safePrepareDemand[prefabIndex];
                    TotalPrepareCreations += poolManager.Prepare(PoolId.SafeMap, safePrefabNames[prefabIndex], desired);
                }
                else
                {
                    int desired = ++normalPrepareDemand[prefabIndex];
                    TotalPrepareCreations += poolManager.Prepare(PoolId.NormalMap, normalPrefabNames[prefabIndex], desired);
                }
            }
        }
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

        float wallZ = ((float)transitionLastChunkIndex + 1f) * chunkLength + transitionWallOffset;
        Vector3 wallPosition = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, wallZ);
        transitionWall = poolManager.Rent(PoolId.TransitionWall, wallPosition, Quaternion.identity);

        if (transitionWall != null && worldMovement != null)
            transitionWall.transform.SetParent(worldMovement.transform, true);

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
        Debug.Log($"MapStreaming | Seed={seed}, Next={nextChunkIndex}, Active={activeChunks.Count}, CoverageReady={HasRequiredCoverage}, Spawned={TotalChunksSpawned}, Recycled={TotalChunksRecycled}, PrepareCreated={TotalPrepareCreations}, FailedAttempts={TotalFailedSpawnAttempts}, NormalActive={activeNormal}, SafeActive={activeSafe}", this);
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
        transitionSafeChunkCount = Mathf.Max(1, transitionSafeChunkCount);
        transitionWallOffset = Mathf.Max(0f, transitionWallOffset);
    }
}
