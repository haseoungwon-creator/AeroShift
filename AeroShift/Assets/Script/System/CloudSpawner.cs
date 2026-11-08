
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CloudSpawner : MonoBehaviour
{
    private const int SpawnsPerFrame = 4;
    private const int PlacementsPerFrame = 50;
    private const int AllowedStalledFrames = 120;

    private struct CloudEntry
    {
        public GameObject Cloud;
        public double RecycleDistance;

        public CloudEntry(
            GameObject cloud,
            double recycleDistance)
        {
            Cloud = cloud;
            RecycleDistance = recycleDistance;
        }
    }

    [Header("References")]
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private WorldMovement worldMovement;

    [Header("Cloud Placement")]
    [SerializeField] private float spawnWidth = 200f;
    [SerializeField] private float spawnDistance = 1400f;
    [SerializeField] private float despawnDistance = 100f;
    [SerializeField] private float spawnSpacing = 6f;
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float height3D = 80f;
    [SerializeField] private float height2D = -20f;

    [Header("Distance Culling")]
    [SerializeField] private bool enableDistanceCulling = true;
    [SerializeField] private string cloudLayerName = "Cloud";
    [SerializeField] private float cloudCullDistance = 900f;
    [SerializeField] private float cullPreloadMargin = 100f;
    [SerializeField] private Camera[] cullingCameras;

    [Header("Group Movement")]
    [SerializeField] private float rootRebaseDistance = 5000f;

    private readonly List<CloudEntry> cloudHeap =
        new List<CloudEntry>(256);

    private readonly Dictionary<Camera, float> originalCullDistances =
        new Dictionary<Camera, float>();

    private Transform activeCloudRoot;

    private double scrollDistance;
    private double nextSpawnDistance;

    private float effectiveSpawnDistance;
    private float initialOriginX;
    private float initialOriginZ;

    private int initialTargetCount;
    private int initialPlacedCount;
    private int nextInitialIndex;
    private int stalledFrames;

    private bool initialized;
    private bool is2D;
    private bool spawnRangeLimited;

    private GameStates lastGameState;
    private bool hasLastGameState;

    private int cloudLayer = -1;

    public int InitialTargetCount => initialTargetCount;
    public int InitialPlacedCount => initialPlacedCount;
    public int ActiveCloudCount => cloudHeap.Count;
    public float EffectiveSpawnDistance => effectiveSpawnDistance;
    public bool SpawnRangeLimited => spawnRangeLimited;
    public int CloudRentFailures { get; private set; }
    public int RuntimeSpawnCount { get; private set; }

    public bool InitialPlacementComplete =>
        initialized && nextInitialIndex < 0;

    private void Start()
    {
        if (poolManager == null || worldMovement == null)
        {
            enabled = false;
            return;
        }

        if (poolManager.GetVariantCount(PoolId.Cloud) == 0)
        {
            enabled = false;
            return;
        }

        GameObject root = new GameObject("CloudActiveRoot");

        activeCloudRoot = root.transform;
        activeCloudRoot.SetParent(transform, false);

        initialized = true;

        ApplyDistanceCulling();
        ConfigureSpawn();
    }

    private void OnEnable()
    {
        if (initialized)
            ApplyDistanceCulling();
    }

    private void OnDisable()
    {
        RestoreDistanceCulling();
    }

    private void Update()
    {
        if (!initialized)
            return;

        GameManager manager = GameManager.Instance;

        if (manager == null)
            return;

        GameStates state = manager.CurrentState;

        if (hasLastGameState &&
            lastGameState != state &&
            state == GameStates.Ready)
        {
            ResetClouds();
        }

        lastGameState = state;
        hasLastGameState = true;

        if (state == GameStates.Ready)
        {
            PlaceInitialClouds();
            return;
        }

        if (state != GameStates.Playing ||
            worldMovement.IsRewinding)
            return;

        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

        float speed = Mathf.Max(
            0f,
            moveSpeed + worldMovement.CurrentSpeed
        );

        float distance = speed * deltaTime;

        if (distance > 0f)
        {
            scrollDistance += distance;

            MoveCloudGroup(distance);
            RecycleClouds();
            RebaseRootIfNeeded();
            SpawnByDistance();
        }

        if (!InitialPlacementComplete)
            PlaceInitialClouds();
    }

    private void ConfigureSpawn()
    {
        effectiveSpawnDistance =
            Mathf.Max(0f, spawnDistance);

        spawnRangeLimited = false;

        if (enableDistanceCulling &&
            cloudLayer >= 0 &&
            originalCullDistances.Count > 0)
        {
            effectiveSpawnDistance = Mathf.Min(
                effectiveSpawnDistance,
                cloudCullDistance + cullPreloadMargin
            );

            spawnRangeLimited =
                effectiveSpawnDistance < spawnDistance;
        }

        spawnSpacing = Mathf.Max(0.1f, spawnSpacing);

        initialOriginX = transform.position.x;
        initialOriginZ = transform.position.z;

        float range =
            effectiveSpawnDistance +
            Mathf.Max(0f, despawnDistance);

        initialTargetCount = Mathf.CeilToInt(
            range / spawnSpacing
        );

        initialPlacedCount = 0;
        nextInitialIndex = initialTargetCount - 1;
        stalledFrames = 0;

        nextSpawnDistance =
            scrollDistance + spawnSpacing;

        if (cloudHeap.Capacity < initialTargetCount)
            cloudHeap.Capacity = initialTargetCount;
    }

    private void PlaceInitialClouds()
    {
        if (InitialPlacementComplete)
            return;

        int processed = 0;

        float despawnZ =
            transform.position.z - despawnDistance;

        while (nextInitialIndex >= 0 &&
               processed < PlacementsPerFrame)
        {
            float z =
                initialOriginZ +
                effectiveSpawnDistance -
                spawnSpacing * nextInitialIndex -
                (float)scrollDistance;

            if (z < despawnZ)
            {
                nextInitialIndex--;
                processed++;
                continue;
            }

            float x = GetRandomX();

            Vector3 position = new Vector3(
                x,
                GetCloudHeight(),
                z
            );

            GameObject cloud =
                poolManager.Rent(PoolId.Cloud, position);

            if (cloud == null)
            {
                CloudRentFailures++;
                stalledFrames++;

                if (stalledFrames >= AllowedStalledFrames)
                    nextInitialIndex = -1;

                return;
            }

            stalledFrames = 0;

            RegisterCloud(cloud, z);

            initialPlacedCount++;
            nextInitialIndex--;
            processed++;
        }
    }

    private void SpawnByDistance()
    {
        double spacing = spawnSpacing;

        double backlog =
            scrollDistance - nextSpawnDistance;

        double allowedBacklog =
            spacing * SpawnsPerFrame;

        if (backlog > allowedBacklog)
        {
            long skipped = (long)Math.Ceiling(
                (backlog - allowedBacklog) / spacing
            );

            nextSpawnDistance += skipped * spacing;
        }

        int processed = 0;

        while (scrollDistance >= nextSpawnDistance &&
               processed < SpawnsPerFrame)
        {
            double overshoot =
                scrollDistance - nextSpawnDistance;

            float z =
                transform.position.z +
                effectiveSpawnDistance -
                (float)overshoot;

            float despawnZ =
                transform.position.z - despawnDistance;

            if (z >= despawnZ)
            {
                Vector3 position = new Vector3(
                    GetRandomX(),
                    GetCloudHeight(),
                    z
                );

                GameObject cloud =
                    poolManager.Rent(PoolId.Cloud, position);

                if (cloud != null)
                {
                    RegisterCloud(cloud, z);
                    RuntimeSpawnCount++;
                }
                else
                {
                    CloudRentFailures++;
                }
            }

            nextSpawnDistance += spacing;
            processed++;
        }
    }

    private void RegisterCloud(
        GameObject cloud,
        float worldZ)
    {
        cloud.transform.SetParent(
            activeCloudRoot,
            true
        );

        double recycleDistance =
            worldZ +
            scrollDistance -
            (transform.position.z - despawnDistance);

        PushCloud(
            new CloudEntry(cloud, recycleDistance)
        );
    }

    private void MoveCloudGroup(float distance)
    {
        activeCloudRoot.position +=
            Vector3.back * distance;
    }

    private void RecycleClouds()
    {
        while (cloudHeap.Count > 0 &&
               cloudHeap[0].RecycleDistance <
               scrollDistance)
        {
            CloudEntry expired = PopCloud();

            if (expired.Cloud != null)
                poolManager.Return(expired.Cloud);
        }
    }

    private void RebaseRootIfNeeded()
    {
        Vector3 rootPosition = activeCloudRoot.position;

        float offsetZ =
            rootPosition.z - transform.position.z;

        if (Mathf.Abs(offsetZ) <
            Mathf.Max(100f, rootRebaseDistance))
            return;

        rootPosition.z = transform.position.z;
        activeCloudRoot.position = rootPosition;

        for (int i = 0; i < cloudHeap.Count; i++)
        {
            GameObject cloud = cloudHeap[i].Cloud;

            if (cloud == null)
                continue;

            Transform target = cloud.transform;
            Vector3 position = target.position;

            position.z += offsetZ;
            target.position = position;
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

            if (parentEntry.RecycleDistance <=
                entry.RecycleDistance)
                break;

            cloudHeap[index] = parentEntry;
            index = parent;
        }

        cloudHeap[index] = entry;
    }

    private CloudEntry PopCloud()
    {
        CloudEntry result = cloudHeap[0];

        int lastIndex = cloudHeap.Count - 1;
        CloudEntry last = cloudHeap[lastIndex];

        cloudHeap.RemoveAt(lastIndex);

        if (lastIndex == 0)
            return result;

        int index = 0;
        int count = cloudHeap.Count;

        while (true)
        {
            int left = index * 2 + 1;

            if (left >= count)
                break;

            int right = left + 1;

            int child =
                right < count &&
                cloudHeap[right].RecycleDistance <
                cloudHeap[left].RecycleDistance
                    ? right
                    : left;

            if (last.RecycleDistance <=
                cloudHeap[child].RecycleDistance)
                break;

            cloudHeap[index] = cloudHeap[child];
            index = child;
        }

        cloudHeap[index] = last;

        return result;
    }

    private void ApplyDistanceCulling()
    {
        if (!enableDistanceCulling)
            return;

        int layer = LayerMask.NameToLayer(cloudLayerName);

        if (layer < 0)
            return;

        cloudLayer = layer;

        if (cullingCameras != null &&
            cullingCameras.Length > 0)
        {
            for (int i = 0; i < cullingCameras.Length; i++)
                ConfigureCamera(cullingCameras[i]);
        }
        else
        {
            ConfigureCamera(Camera.main);
        }
    }

    private void ConfigureCamera(Camera camera)
    {
        if (camera == null)
            return;

        float[] distances = camera.layerCullDistances;

        if (distances == null || distances.Length != 32)
            return;

        if (!originalCullDistances.ContainsKey(camera))
        {
            originalCullDistances.Add(
                camera,
                distances[cloudLayer]
            );
        }

        distances[cloudLayer] = cloudCullDistance;
        camera.layerCullDistances = distances;
    }

    private void RestoreDistanceCulling()
    {
        if (cloudLayer < 0)
            return;

        foreach (
            KeyValuePair<Camera, float> pair
            in originalCullDistances)
        {
            Camera camera = pair.Key;

            if (camera == null)
                continue;

            float[] distances =
                camera.layerCullDistances;

            if (distances == null || distances.Length != 32)
                continue;

            distances[cloudLayer] = pair.Value;
            camera.layerCullDistances = distances;
        }

        originalCullDistances.Clear();
        cloudLayer = -1;
    }

    private float GetRandomX()
    {
        return initialOriginX +
            UnityEngine.Random.Range(
                -spawnWidth * 0.5f,
                spawnWidth * 0.5f
            );
    }

    private float GetCloudHeight()
    {
        return is2D ? height2D : height3D;
    }

    public void Set2DMode()
    {
        if (is2D)
            return;

        float previous = GetCloudHeight();
        is2D = true;

        ChangeCloudHeight(
            GetCloudHeight() - previous
        );
    }

    public void Set3DMode()
    {
        if (!is2D)
            return;

        float previous = GetCloudHeight();
        is2D = false;

        ChangeCloudHeight(
            GetCloudHeight() - previous
        );
    }

    private void ChangeCloudHeight(float difference)
    {
        if (activeCloudRoot == null)
            return;

        activeCloudRoot.position +=
            Vector3.up * difference;
    }

    public void ResetClouds()
    {
        if (!initialized)
            return;

        for (int i = 0; i < cloudHeap.Count; i++)
        {
            GameObject cloud = cloudHeap[i].Cloud;

            if (cloud != null)
                poolManager.Return(cloud);
        }

        cloudHeap.Clear();

        activeCloudRoot.localPosition = Vector3.zero;

        scrollDistance = 0d;
        is2D = false;

        CloudRentFailures = 0;
        RuntimeSpawnCount = 0;

        ConfigureSpawn();
    }
}
