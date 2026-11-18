using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class MapManager : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private PlayerRewind playerRewind;

    [SerializeField] private float chunkLength = 80f;
    [SerializeField] private Vector2 mapOriginXY = Vector2.zero;
    [SerializeField] private int forwardChunkCount = 6;
    [SerializeField] private int startingSafeChunkCount = 3;
    [SerializeField] private float keepBehindDistance = 160f;
    [SerializeField] private int maxSpawnsPerFrame = 2;
    [SerializeField] private int prepareLookaheadChunks = 4;

    [SerializeField] private int transitionSafeChunkCount = 3;
    [SerializeField] private bool spawnTransitionWall = true;
    [SerializeField] private float transitionWallOffset = 30f;

    [SerializeField] private bool enableProximityCollisionCulling;
    [SerializeField] private LayerMask obstacleCollisionLayers = 1;
    [SerializeField] private float collisionEnableAhead = 160f;
    [SerializeField] private float collisionKeepBehind = 80f;
    [SerializeField] private float collisionCheckInterval = 0.1f;

    private int seed;
    [SerializeField] private bool allow180DegreeRotation;

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

    public int CurrentSeed { get { return seed; } }
    public int NextChunkIndex { get { return nextChunkIndex; } }
    public int ActiveChunkCount { get { return activeChunks.Count; } }
    public float ChunkLength { get { return chunkLength; } }
    public bool HasRequiredCoverage
    {
        get
        {
            return initialized && is3DMapActive && activeChunks.Count > 0 &&
                ((double)nextChunkIndex - 0.5d) * chunkLength >=
                (double)LogicalPlayerZ + forwardChunkCount * chunkLength;
        }
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
        if (normalVariants.Length == 0 || (startingSafeChunkCount > 0 && safeVariants.Length == 0))
        {
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
        int[] variants = safe ? safeVariants : normalVariants;
        if (variants.Length == 0)
            return false;

        int choice = safe ? 0 : ChunkSeedUtility.SelectNormalPrefab(seed, nextChunkIndex, variants.Length);
        if (choice < 0 || choice >= variants.Length)
            return false;

        float centerZ = nextChunkIndex * chunkLength;
        Vector3 position = WorldOrigin + new Vector3(mapOriginXY.x, mapOriginXY.y, centerZ);
        bool rotated = !safe && allow180DegreeRotation && ChunkSeedUtility.ShouldRotate180(seed, nextChunkIndex);
        Quaternion rotation = rotated ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        PoolId id = safe ? PoolId.SafeMap : PoolId.NormalMap;
        Transform parent = worldMovement != null ? worldMovement.transform : null;
        GameObject instance = poolManager.RentInactive(id, position, rotation, parent, variants[choice]);
        if (instance == null)
            return false;

        if (!instance.TryGetComponent(out MapChunk chunk))
            chunk = instance.AddComponent<MapChunk>();
        chunk.SetChunkData(nextChunkIndex, centerZ - chunkLength * 0.5f, chunkLength, safe, rotated);

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

            int[] variants = safe ? safeVariants : normalVariants;
            if (variants.Length == 0)
                continue;
            int choice = safe ? 0 : ChunkSeedUtility.SelectNormalPrefab(seed, index, variants.Length);
            if (choice < 0 || choice >= variants.Length)
                continue;
            if (safe)
                safeDemand[choice]++;
            else
                normalDemand[choice]++;
        }

        PrepareVariants(PoolId.NormalMap, normalVariants, normalDemand);
        PrepareVariants(PoolId.SafeMap, safeVariants, safeDemand);
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

    public bool IsTransitionMapArrived(float unused)
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
        return GameManager.Instance != null && GameManager.Instance.CurrentState == GameStates.Playing &&
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
}
