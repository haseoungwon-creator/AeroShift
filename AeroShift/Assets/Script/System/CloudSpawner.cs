
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class CloudSpawner : MonoBehaviour
{
    private static readonly ProfilerMarker InitialPlacementMarker =
        new ProfilerMarker("AeroShift.Cloud.InitialPlacement");

    private static readonly ProfilerMarker MoveMarker =
        new ProfilerMarker("AeroShift.Cloud.MoveAndRecycle");

    private static readonly ProfilerMarker GroupMoveMarker =
        new ProfilerMarker("AeroShift.Cloud.GroupedMove");

    private static readonly ProfilerMarker IndividualMoveMarker =
        new ProfilerMarker("AeroShift.Cloud.IndividualMove");

    private static readonly ProfilerMarker RecycleMarker =
        new ProfilerMarker("AeroShift.Cloud.RecycleExpired");

    private static readonly ProfilerMarker RebaseMarker =
        new ProfilerMarker("AeroShift.Cloud.RebaseRoot");

    private static readonly ProfilerMarker SpawnMarker =
        new ProfilerMarker("AeroShift.Cloud.Spawn");

    private static readonly ProfilerMarker ResetMarker =
        new ProfilerMarker("AeroShift.Cloud.Reset");

    private struct CloudEntry
    {
        public GameObject Cloud;
        public double RecycleAtDistance;

        public CloudEntry(GameObject cloud, double recycleAtDistance)
        {
            Cloud = cloud;
            RecycleAtDistance = recycleAtDistance;
        }
    }

    [Header("Pool")]
    [SerializeField] private PoolManager poolManager;

    [Header("Spawn")]
    [SerializeField, Min(0f)] private float spawnWidth = 200f;
    [SerializeField, Min(0f)] private float spawnDistance = 1400f;
    [SerializeField, Min(0.01f)] private float spawnInterval = 0.1f;
    [SerializeField, Min(0.01f)] private float moveSpeed = 20f;
    [SerializeField, Min(0f)] private float despawnDistance = 70f;
    [SerializeField] private float height3D = 80f;
    [SerializeField] private float height2D = -20f;
    [SerializeField] private WorldMovement worldMovement;

    [Header("Initial Placement")]
    [SerializeField, Min(1)] private int maxInitialPlacementsPerFrame = 50;
    [SerializeField, Min(1)] private int maxStalledFrames = 120;

    [Header("Movement Optimization")]
    [SerializeField] private bool useGroupedMovement = true;
    [SerializeField, Min(100f)] private float rootRebaseDistance = 5000f;

    [Header("Visibility")]
    [SerializeField] private bool enableDistanceCulling = true;
    [SerializeField] private string cloudLayerName = "Cloud";
    [SerializeField, Min(1f)] private float cloudCullDistance = 900f;
    [SerializeField] private Camera[] cullingCameras;

    [Header("Spawn Optimization")]
    [SerializeField] private bool restrictSpawnToCullRange;
    [SerializeField, Min(0f)] private float cullPreloadMargin = 100f;

    private readonly List<CloudEntry> cloudHeap =
        new List<CloudEntry>(800);

    private readonly Dictionary<Camera, float> originalCullingDistances =
        new Dictionary<Camera, float>();

    private Transform activeCloudRoot;
    private float spawnTimer;
    private float effectiveSpawnDistance;
    private double accumulatedScrollDistance;

    private float initialOriginX;
    private float initialOriginZ;
    private float initialSpacing;

    private int nextInitialIndex = -1;
    private int initialTargetCount;
    private int initialPlacedCount;
    private int stalledFrames;

    private bool isInitialized;
    private bool is2D;
    private bool spawnRangeLimited;

    private int configuredCloudLayer = -1;
    private GameStates lastObservedGameState;
    private bool hasObservedGameState;

    public bool InitialPlacementComplete =>
        isInitialized && nextInitialIndex < 0;

    public int InitialTargetCount => initialTargetCount;
    public int InitialPlacedCount => initialPlacedCount;
    public int ActiveCloudCount => cloudHeap.Count;

    public float EffectiveSpawnDistance => effectiveSpawnDistance;
    public bool SpawnRangeLimited => spawnRangeLimited;

    public int GroupedMoveFrameCount { get; private set; }
    public int IndividualMoveFrameCount { get; private set; }
    public int CloudRentFailures { get; private set; }
    public int RuntimeSpawnCount { get; private set; }

    private void Start()
    {
        InitializeClouds();

        if (!isInitialized)
            return;

        ApplyCloudDistanceCulling();
        ConfigureSpawnRange();
    }

    private void OnEnable()
    {
        if (isInitialized)
            ApplyCloudDistanceCulling();
    }

    private void OnDisable()
    {
        RestoreCloudDistanceCulling();
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        GameManager gameManager = GameManager.Instance;

        if (gameManager == null)
            return;

        GameStates currentState = gameManager.CurrentState;

        if (hasObservedGameState &&
            lastObservedGameState != currentState &&
            currentState == GameStates.Ready)
        {
            using (ResetMarker.Auto())
                ResetClouds();
        }

        lastObservedGameState = currentState;
        hasObservedGameState = true;

        if (currentState == GameStates.Ready)
        {
            if (!InitialPlacementComplete)
            {
                using (InitialPlacementMarker.Auto())
                    PlaceInitialClouds();
            }

            return;
        }

        if (currentState != GameStates.Playing ||
            worldMovement.IsRewinding)
            return;

        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

        float moveDistance =
            Mathf.Max(0f, moveSpeed + worldMovement.CurrentSpeed)
            * deltaTime;

        accumulatedScrollDistance += moveDistance;

        using (MoveMarker.Auto())
            MoveAndRecycleClouds(moveDistance);

        using (SpawnMarker.Auto())
            SpawnClouds(deltaTime);

        if (!InitialPlacementComplete)
        {
            using (InitialPlacementMarker.Auto())
                PlaceInitialClouds();
        }
    }

    private void InitializeClouds()
    {
        if (poolManager == null ||
            worldMovement == null ||
            spawnInterval <= 0f ||
            moveSpeed <= 0f)
        {
            Debug.LogError("CloudSpawner 설정을 확인하세요.", this);
            enabled = false;
            return;
        }

        if (poolManager.GetVariantCount(PoolId.Cloud) == 0)
        {
            Debug.LogError("PoolManager에 Cloud 그룹이 없습니다.", this);
            enabled = false;
            return;
        }

        GameObject root = new GameObject("CloudActiveRoot");
        activeCloudRoot = root.transform;
        activeCloudRoot.SetParent(transform, false);

        isInitialized = true;
    }

    private void ConfigureSpawnRange()
    {
        effectiveSpawnDistance = spawnDistance;
        spawnRangeLimited = false;

        if (restrictSpawnToCullRange &&
            enableDistanceCulling &&
            configuredCloudLayer >= 0 &&
            originalCullingDistances.Count > 0)
        {
            float limitedDistance =
                cloudCullDistance + cullPreloadMargin;

            effectiveSpawnDistance = Mathf.Min(
                spawnDistance,
                limitedDistance
            );

            spawnRangeLimited =
                effectiveSpawnDistance < spawnDistance;
        }

        initialOriginX = transform.position.x;
        initialOriginZ = transform.position.z;

        initialSpacing = Mathf.Max(
            0.001f,
            moveSpeed * spawnInterval
        );

        float range =
            effectiveSpawnDistance + despawnDistance;

        initialTargetCount = Mathf.Max(
            0,
            Mathf.CeilToInt(range / initialSpacing)
        );

        nextInitialIndex = initialTargetCount - 1;
        initialPlacedCount = 0;
        stalledFrames = 0;

        if (cloudHeap.Capacity < initialTargetCount)
            cloudHeap.Capacity = initialTargetCount;
    }

    private void PlaceInitialClouds()
    {
        int processed = 0;

        float despawnZ =
            transform.position.z - despawnDistance;

        while (nextInitialIndex >= 0 &&
               processed < maxInitialPlacementsPerFrame)
        {
            float z =
                initialOriginZ +
                effectiveSpawnDistance -
                initialSpacing * nextInitialIndex -
                (float)accumulatedScrollDistance;

            if (z < despawnZ)
            {
                nextInitialIndex--;
                processed++;
                continue;
            }

            float x = initialOriginX +
                Random.Range(
                    -spawnWidth * 0.5f,
                    spawnWidth * 0.5f
                );

            Vector3 position =
                new Vector3(x, GetCloudHeight(), z);

            GameObject cloud =
                poolManager.Rent(PoolId.Cloud, position);

            if (cloud == null)
            {
                CloudRentFailures++;
                stalledFrames++;

                if (stalledFrames >= maxStalledFrames)
                {
                    Debug.LogWarning(
                        "구름 초기 배치가 중단되었습니다.",
                        this
                    );

                    nextInitialIndex = -1;
                }

                return;
            }

            stalledFrames = 0;

            RegisterCloud(cloud, z);

            initialPlacedCount++;
            nextInitialIndex--;
            processed++;
        }
    }

    private void SpawnClouds(float deltaTime)
    {
        spawnTimer = Mathf.Min(
            spawnTimer + deltaTime,
            spawnInterval
        );

        if (spawnTimer < spawnInterval)
            return;

        float x = transform.position.x +
            Random.Range(
                -spawnWidth * 0.5f,
                spawnWidth * 0.5f
            );

        float z =
            transform.position.z + effectiveSpawnDistance;

        Vector3 position =
            new Vector3(x, GetCloudHeight(), z);

        GameObject cloud =
            poolManager.Rent(PoolId.Cloud, position);

        if (cloud == null)
        {
            CloudRentFailures++;
            return;
        }

        RegisterCloud(cloud, z);

        RuntimeSpawnCount++;
        spawnTimer = 0f;
    }

    private void RegisterCloud(GameObject cloud, float worldZ)
    {
        cloud.transform.SetParent(activeCloudRoot, true);

        double recycleAt =
            worldZ +
            accumulatedScrollDistance -
            (transform.position.z - despawnDistance);

        PushCloud(new CloudEntry(cloud, recycleAt));
    }

    private void MoveAndRecycleClouds(float moveDistance)
    {
        if (useGroupedMovement)
        {
            using (GroupMoveMarker.Auto())
            {
                GroupedMoveFrameCount++;

                activeCloudRoot.position +=
                    Vector3.back * moveDistance;
            }
        }
        else
        {
            using (IndividualMoveMarker.Auto())
            {
                IndividualMoveFrameCount++;

                for (int i = 0; i < cloudHeap.Count; i++)
                {
                    GameObject cloud = cloudHeap[i].Cloud;

                    if (cloud == null)
                        continue;

                    Transform cloudTransform = cloud.transform;

                    Vector3 position = cloudTransform.position;
                    position.z -= moveDistance;

                    cloudTransform.position = position;
                }
            }
        }

        using (RecycleMarker.Auto())
        {
            while (cloudHeap.Count > 0 &&
                   cloudHeap[0].RecycleAtDistance <
                   accumulatedScrollDistance)
            {
                CloudEntry expired = PopCloud();

                if (expired.Cloud != null)
                    poolManager.Return(expired.Cloud);
            }
        }

        if (useGroupedMovement)
        {
            using (RebaseMarker.Auto())
                RebaseRootIfNecessary();
        }
    }

    private void PushCloud(CloudEntry entry)
    {
        int index = cloudHeap.Count;

        cloudHeap.Add(entry);

        while (index > 0)
        {
            int parent = (index - 1) / 2;

            CloudEntry parentEntry = cloudHeap[parent];

            if (parentEntry.RecycleAtDistance <=
                entry.RecycleAtDistance)
                break;

            cloudHeap[index] = parentEntry;
            index = parent;
        }

        cloudHeap[index] = entry;
    }

    private CloudEntry PopCloud()
    {
        CloudEntry first = cloudHeap[0];

        int lastIndex = cloudHeap.Count - 1;

        CloudEntry tail = cloudHeap[lastIndex];

        cloudHeap.RemoveAt(lastIndex);

        if (lastIndex == 0)
            return first;

        int index = 0;
        int count = cloudHeap.Count;

        while (true)
        {
            int left = index * 2 + 1;

            if (left >= count)
                break;

            int right = left + 1;

            int smallest =
                right < count &&
                cloudHeap[right].RecycleAtDistance <
                cloudHeap[left].RecycleAtDistance
                    ? right
                    : left;

            if (tail.RecycleAtDistance <=
                cloudHeap[smallest].RecycleAtDistance)
                break;

            cloudHeap[index] = cloudHeap[smallest];
            index = smallest;
        }

        cloudHeap[index] = tail;

        return first;
    }

    private void RebaseRootIfNecessary()
    {
        Vector3 rootPosition = activeCloudRoot.position;

        float offsetZ =
            rootPosition.z - transform.position.z;

        if (Mathf.Abs(offsetZ) < rootRebaseDistance)
            return;

        rootPosition.z = transform.position.z;

        activeCloudRoot.position = rootPosition;

        for (int i = 0; i < cloudHeap.Count; i++)
        {
            GameObject cloud = cloudHeap[i].Cloud;

            if (cloud == null)
                continue;

            Transform cloudTransform = cloud.transform;

            Vector3 position = cloudTransform.position;
            position.z += offsetZ;

            cloudTransform.position = position;
        }
    }

    private void ResetClouds()
    {
        for (int i = 0; i < cloudHeap.Count; i++)
        {
            GameObject cloud = cloudHeap[i].Cloud;

            if (cloud != null)
                poolManager.Return(cloud);
        }

        cloudHeap.Clear();

        activeCloudRoot.localPosition = Vector3.zero;

        spawnTimer = 0f;
        accumulatedScrollDistance = 0d;
        is2D = false;

        GroupedMoveFrameCount = 0;
        IndividualMoveFrameCount = 0;
        CloudRentFailures = 0;
        RuntimeSpawnCount = 0;

        ConfigureSpawnRange();
    }

    private void ApplyCloudDistanceCulling()
    {
        if (!enableDistanceCulling)
            return;

        int cloudLayer =
            LayerMask.NameToLayer(cloudLayerName);

        if (cloudLayer < 0)
        {
            Debug.LogWarning(
                $"'{cloudLayerName}' Layer가 없습니다.",
                this
            );

            return;
        }

        configuredCloudLayer = cloudLayer;

        if (cullingCameras != null &&
            cullingCameras.Length > 0)
        {
            for (int i = 0; i < cullingCameras.Length; i++)
                ConfigureCamera(cullingCameras[i], cloudLayer);
        }
        else
        {
            ConfigureCamera(Camera.main, cloudLayer);
        }
    }

    private void ConfigureCamera(
        Camera targetCamera,
        int cloudLayer)
    {
        if (targetCamera == null)
            return;

        float[] distances = targetCamera.layerCullDistances;

        if (distances == null || distances.Length != 32)
            return;

        if (!originalCullingDistances.ContainsKey(targetCamera))
        {
            originalCullingDistances.Add(
                targetCamera,
                distances[cloudLayer]
            );
        }

        distances[cloudLayer] = cloudCullDistance;

        targetCamera.layerCullDistances = distances;
    }

    private void RestoreCloudDistanceCulling()
    {
        if (configuredCloudLayer < 0)
            return;

        foreach (
            KeyValuePair<Camera, float> pair
            in originalCullingDistances)
        {
            Camera targetCamera = pair.Key;

            if (targetCamera == null)
                continue;

            float[] distances =
                targetCamera.layerCullDistances;

            if (distances == null || distances.Length != 32)
                continue;

            distances[configuredCloudLayer] = pair.Value;

            targetCamera.layerCullDistances = distances;
        }

        originalCullingDistances.Clear();
        configuredCloudLayer = -1;
    }

    private void OnValidate()
    {
        spawnInterval = Mathf.Max(0.01f, spawnInterval);
        moveSpeed = Mathf.Max(0.01f, moveSpeed);

        maxInitialPlacementsPerFrame = Mathf.Max(
            1,
            maxInitialPlacementsPerFrame
        );

        maxStalledFrames = Mathf.Max(1, maxStalledFrames);

        rootRebaseDistance =
            Mathf.Max(100f, rootRebaseDistance);

        cloudCullDistance =
            Mathf.Max(1f, cloudCullDistance);

        cullPreloadMargin =
            Mathf.Max(0f, cullPreloadMargin);

        if (!Application.isPlaying || !isInitialized)
            return;

        RestoreCloudDistanceCulling();

        if (isActiveAndEnabled)
            ApplyCloudDistanceCulling();
    }

    private float GetCloudHeight()
    {
        return is2D ? height2D : height3D;
    }

    public void Set2DMode()
    {
        if (is2D)
            return;

        float previousHeight = GetCloudHeight();

        is2D = true;

        SetCloudHeight(
            GetCloudHeight() - previousHeight
        );
    }

    public void Set3DMode()
    {
        if (!is2D)
            return;

        float previousHeight = GetCloudHeight();

        is2D = false;

        SetCloudHeight(
            GetCloudHeight() - previousHeight
        );
    }

    private void SetCloudHeight(float deltaHeight)
    {
        if (activeCloudRoot != null)
        {
            activeCloudRoot.position +=
                Vector3.up * deltaHeight;
        }
    }
}
