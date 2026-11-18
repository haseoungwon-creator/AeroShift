using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CloudSpawner : MonoBehaviour
{
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private float spawnWidth = 200f;
    [SerializeField] private float spawnDistance = 1400f;
    [SerializeField] private float despawnDistance = 100f;
    [SerializeField] private float spawnSpacing = 6f;
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float height3D = 80f;
    [SerializeField] private float height2D = -20f;
    [SerializeField] private bool enableDistanceCulling = true;
    [SerializeField] private string cloudLayerName = "Cloud";
    [SerializeField] private float cloudCullDistance = 900f;
    [SerializeField] private float cullPreloadMargin = 100f;
    [SerializeField] private Camera[] cullingCameras;
    [SerializeField] private float rootRebaseDistance = 5000f;

    private struct CloudItem
    {
        public GameObject Object;
        public double RecycleAt;

        public CloudItem(GameObject instance, double recycleAt)
        {
            Object = instance;
            RecycleAt = recycleAt;
        }
    }

    private readonly Queue<CloudItem> initialClouds = new Queue<CloudItem>(256);
    private readonly Queue<CloudItem> runtimeClouds = new Queue<CloudItem>(256);
    private readonly Dictionary<Camera, float> originalCullDistances = new Dictionary<Camera, float>();
    private Transform cloudRoot;
    private double scrollDistance;
    private double nextSpawnDistance;
    private float effectiveSpawnDistance;
    private float originX;
    private float originZ;
    private int initialTargetCount;
    private int initialPlacedCount;
    private int initialIndex;
    private int stalledFrames;
    private int cloudLayer = -1;
    private bool initialized;
    private bool is2D;
    private bool spawnRangeLimited;
    private GameStates lastState;
    private bool hasLastState;

    public bool DistanceCullingEnabled { get { return enableDistanceCulling; } }
    public int InitialTargetCount { get { return initialTargetCount; } }
    public int InitialPlacedCount { get { return initialPlacedCount; } }
    public int ActiveCloudCount { get { return initialClouds.Count + runtimeClouds.Count; } }
    public float EffectiveSpawnDistance { get { return effectiveSpawnDistance; } }
    public bool SpawnRangeLimited { get { return spawnRangeLimited; } }
    public int CloudRentFailures { get; private set; }
    public int RuntimeSpawnCount { get; private set; }
    public bool InitialPlacementComplete { get { return initialized && initialIndex < 0; } }

    private void Start()
    {
        if (poolManager == null || worldMovement == null || poolManager.GetVariantCount(PoolId.Cloud) == 0)
        {
            enabled = false;
            return;
        }

        GameObject root = new GameObject("CloudActiveRoot");
        cloudRoot = root.transform;
        cloudRoot.SetParent(transform, false);
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
        if (!initialized || GameManager.Instance == null)
            return;

        GameStates state = GameManager.Instance.CurrentState;
        if (hasLastState && state != lastState && state == GameStates.Ready)
            ResetClouds();
        lastState = state;
        hasLastState = true;

        if (state == GameStates.Ready)
        {
            PlaceInitialClouds();
            return;
        }
        if (state != GameStates.Playing || worldMovement.IsRewinding)
            return;

        float distance = Mathf.Max(0f, moveSpeed + worldMovement.CurrentSpeed) * Time.deltaTime;
        if (distance <= 0f)
            return;

        scrollDistance += distance;
        cloudRoot.position += Vector3.back * distance;
        Recycle(initialClouds);
        Recycle(runtimeClouds);
        RebaseRoot();

        if (!InitialPlacementComplete)
            PlaceInitialClouds();
        if (InitialPlacementComplete)
            SpawnByDistance();
    }

    private void ConfigureSpawn()
    {
        effectiveSpawnDistance = Mathf.Max(0f, spawnDistance);
        spawnRangeLimited = false;
        if (enableDistanceCulling && cloudLayer >= 0 && originalCullDistances.Count > 0)
        {
            effectiveSpawnDistance = Mathf.Min(effectiveSpawnDistance, cloudCullDistance + cullPreloadMargin);
            spawnRangeLimited = effectiveSpawnDistance < spawnDistance;
        }

        spawnSpacing = Mathf.Max(0.1f, spawnSpacing);
        originX = transform.position.x;
        originZ = transform.position.z;
        initialTargetCount = Mathf.CeilToInt((effectiveSpawnDistance + Mathf.Max(0f, despawnDistance)) / spawnSpacing);
        initialPlacedCount = 0;
        initialIndex = initialTargetCount - 1;
        stalledFrames = 0;
        nextSpawnDistance = scrollDistance + spawnSpacing;
    }

    private void PlaceInitialClouds()
    {
        int processed = 0;
        while (initialIndex >= 0 && processed < 50)
        {
            float z = originZ + effectiveSpawnDistance - spawnSpacing * initialIndex - (float)scrollDistance;
            if (z < transform.position.z - despawnDistance)
            {
                initialIndex--;
                processed++;
                continue;
            }
            if (!SpawnCloud(z, initialClouds))
            {
                CloudRentFailures++;
                stalledFrames++;
                if (stalledFrames >= 120)
                    initialIndex = -1;
                return;
            }
            stalledFrames = 0;
            initialPlacedCount++;
            initialIndex--;
            processed++;
        }
    }

    private void SpawnByDistance()
    {
        double spacing = spawnSpacing;
        double backlog = scrollDistance - nextSpawnDistance;
        if (backlog > spacing * 4d)
            nextSpawnDistance += Math.Ceiling((backlog - spacing * 4d) / spacing) * spacing;

        int processed = 0;
        while (scrollDistance >= nextSpawnDistance && processed < 4)
        {
            float z = transform.position.z + effectiveSpawnDistance - (float)(scrollDistance - nextSpawnDistance);
            if (z >= transform.position.z - despawnDistance)
            {
                if (SpawnCloud(z, runtimeClouds))
                    RuntimeSpawnCount++;
                else
                    CloudRentFailures++;
            }
            nextSpawnDistance += spacing;
            processed++;
        }
    }

    private bool SpawnCloud(float z, Queue<CloudItem> queue)
    {
        float x = originX + UnityEngine.Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
        float y = is2D ? height2D : height3D;
        GameObject cloud = poolManager.RentInactive(PoolId.Cloud, new Vector3(x, y, z), cloudRoot);
        if (cloud == null)
            return false;

        double recycleAt = z + scrollDistance - (transform.position.z - despawnDistance);
        queue.Enqueue(new CloudItem(cloud, recycleAt));
        cloud.SetActive(true);
        return true;
    }

    private void Recycle(Queue<CloudItem> queue)
    {
        while (queue.Count > 0 && queue.Peek().RecycleAt < scrollDistance)
        {
            CloudItem cloud = queue.Dequeue();
            if (cloud.Object != null)
                poolManager.Return(cloud.Object);
        }
    }

    private void RebaseRoot()
    {
        float offset = cloudRoot.position.z - transform.position.z;
        if (Mathf.Abs(offset) < Mathf.Max(100f, rootRebaseDistance))
            return;

        Vector3 rootPosition = cloudRoot.position;
        rootPosition.z = transform.position.z;
        cloudRoot.position = rootPosition;
        RebaseClouds(initialClouds, offset);
        RebaseClouds(runtimeClouds, offset);
    }

    private void RebaseClouds(Queue<CloudItem> queue, float offset)
    {
        foreach (CloudItem item in queue)
        {
            if (item.Object == null)
                continue;
            Vector3 position = item.Object.transform.position;
            position.z += offset;
            item.Object.transform.position = position;
        }
    }

    public void Set2DMode()
    {
        if (is2D)
            return;
        is2D = true;
        ChangeCloudHeight(height2D - height3D);
    }

    public void Set3DMode()
    {
        if (!is2D)
            return;
        is2D = false;
        ChangeCloudHeight(height3D - height2D);
    }

    private void ChangeCloudHeight(float difference)
    {
        if (cloudRoot != null)
            cloudRoot.position += Vector3.up * difference;
    }

    public void ResetClouds()
    {
        if (!initialized)
            return;
        ReturnClouds(initialClouds);
        ReturnClouds(runtimeClouds);
        cloudRoot.localPosition = Vector3.zero;
        scrollDistance = 0d;
        is2D = false;
        CloudRentFailures = 0;
        RuntimeSpawnCount = 0;
        ConfigureSpawn();
    }

    private void ReturnClouds(Queue<CloudItem> queue)
    {
        while (queue.Count > 0)
        {
            CloudItem cloud = queue.Dequeue();
            if (cloud.Object != null)
                poolManager.Return(cloud.Object);
        }
    }

    private void ApplyDistanceCulling()
    {
        if (!enableDistanceCulling)
            return;
        int layer = LayerMask.NameToLayer(cloudLayerName);
        if (layer < 0)
            return;
        cloudLayer = layer;
        if (cullingCameras != null && cullingCameras.Length > 0)
        {
            for (int i = 0; i < cullingCameras.Length; i++)
                SetCameraCullDistance(cullingCameras[i]);
        }
        else
        {
            SetCameraCullDistance(Camera.main);
        }
    }

    private void SetCameraCullDistance(Camera camera)
    {
        if (camera == null)
            return;
        float[] distances = camera.layerCullDistances;
        if (distances == null || distances.Length != 32)
            return;
        if (!originalCullDistances.ContainsKey(camera))
            originalCullDistances.Add(camera, distances[cloudLayer]);
        distances[cloudLayer] = cloudCullDistance;
        camera.layerCullDistances = distances;
    }

    private void RestoreDistanceCulling()
    {
        if (cloudLayer < 0)
            return;
        foreach (KeyValuePair<Camera, float> item in originalCullDistances)
        {
            Camera camera = item.Key;
            if (camera == null)
                continue;
            float[] distances = camera.layerCullDistances;
            if (distances == null || distances.Length != 32)
                continue;
            distances[cloudLayer] = item.Value;
            camera.layerCullDistances = distances;
        }
        originalCullDistances.Clear();
        cloudLayer = -1;
    }
}
