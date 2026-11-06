using System.Collections.Generic;
using UnityEngine;

public class CloudSpawner : MonoBehaviour
{
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

    [Header("Visibility")]
    [SerializeField] private bool enableDistanceCulling = true;
    [SerializeField] private string cloudLayerName = "Cloud";
    [SerializeField, Min(1f)] private float cloudCullDistance = 900f;
    [SerializeField] private Camera[] cullingCameras;

    private readonly List<GameObject> activeClouds = new List<GameObject>(800);
    private readonly Dictionary<Camera, float> originalCullingDistances = new Dictionary<Camera, float>();
    private float spawnTimer;
    private float accumulatedScrollDistance;
    private float initialOriginX;
    private float initialOriginZ;
    private float initialSpacing;
    private int nextInitialIndex = -1;
    private int initialTargetCount;
    private int initialPlacedCount;
    private int stalledFrames;
    private bool isInitialized;
    private bool is2D;
    private int configuredCloudLayer = -1;

    public bool InitialPlacementComplete => isInitialized && nextInitialIndex < 0;
    public int InitialTargetCount => initialTargetCount;
    public int InitialPlacedCount => initialPlacedCount;
    public int ActiveCloudCount => activeClouds.Count;

    private void Start()
    {
        InitializeClouds();

        if (isInitialized)
            ApplyCloudDistanceCulling();
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

        float deltaTime = Time.deltaTime;
        float moveDistance = (moveSpeed + worldMovement.CurrentSpeed) * deltaTime;

        accumulatedScrollDistance += moveDistance;
        MoveClouds(moveDistance);

        SpawnClouds(deltaTime);

        if (!InitialPlacementComplete)
            PlaceInitialClouds();
    }

    private void InitializeClouds()
    {
        if (poolManager == null || worldMovement == null || spawnInterval <= 0f || moveSpeed <= 0f)
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

        initialOriginX = transform.position.x;
        initialOriginZ = transform.position.z;
        initialSpacing = moveSpeed * spawnInterval;
        initialTargetCount = Mathf.CeilToInt((spawnDistance + despawnDistance) / initialSpacing);
        nextInitialIndex = initialTargetCount - 1;
        activeClouds.Capacity = Mathf.Max(activeClouds.Capacity, initialTargetCount);
        isInitialized = true;
    }

    private void PlaceInitialClouds()
    {
        int processed = 0;
        float despawnZ = transform.position.z - despawnDistance;

        while (nextInitialIndex >= 0 && processed < maxInitialPlacementsPerFrame)
        {
            float z = initialOriginZ + spawnDistance
                - initialSpacing * nextInitialIndex - accumulatedScrollDistance;

            if (z < despawnZ)
            {
                nextInitialIndex--;
                processed++;
                continue;
            }

            float x = initialOriginX + Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
            Vector3 position = new Vector3(x, GetCloudHeight(), z);
            GameObject cloud = poolManager.Rent(PoolId.Cloud, position);

            if (cloud == null)
            {
                stalledFrames++;

                if (stalledFrames >= maxStalledFrames)
                {
                    Debug.LogWarning("구름 초기 배치가 중단되었습니다. Cloud 풀 용량과 확장 설정을 확인하세요.", this);
                    nextInitialIndex = -1;
                }

                return;
            }

            stalledFrames = 0;
            activeClouds.Add(cloud);
            initialPlacedCount++;
            nextInitialIndex--;
            processed++;
        }
    }

    private void SpawnClouds(float deltaTime)
    {
        spawnTimer = Mathf.Min(spawnTimer + deltaTime, spawnInterval);

        if (spawnTimer < spawnInterval)
            return;

        float x = transform.position.x + Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
        Vector3 position = new Vector3(x, GetCloudHeight(), transform.position.z + spawnDistance);
        GameObject cloud = poolManager.Rent(PoolId.Cloud, position);

        if (cloud != null)
        {
            activeClouds.Add(cloud);
            spawnTimer = 0f;
        }
    }

    private void MoveClouds(float moveDistance)
    {
        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            GameObject cloud = activeClouds[i];

            if (cloud == null)
            {
                activeClouds.RemoveAt(i);
                continue;
            }

            Transform cloudTransform = cloud.transform;
            cloudTransform.position += Vector3.back * moveDistance;

            if (cloudTransform.position.z < transform.position.z - despawnDistance)
            {
                poolManager.Return(cloud);
                activeClouds.RemoveAt(i);
            }
        }
    }

    private void ApplyCloudDistanceCulling()
    {
        if (!enableDistanceCulling)
            return;

        int cloudLayer = LayerMask.NameToLayer(cloudLayerName);

        if (cloudLayer < 0)
        {
            Debug.LogWarning($"'{cloudLayerName}' Layer가 없습니다. 구름 거리 컬링을 적용하지 않습니다.", this);
            return;
        }

        configuredCloudLayer = cloudLayer;

        if (cullingCameras != null && cullingCameras.Length > 0)
        {
            for (int i = 0; i < cullingCameras.Length; i++)
                ConfigureCamera(cullingCameras[i], cloudLayer);
        }
        else
        {
            ConfigureCamera(Camera.main, cloudLayer);
        }
    }

    private void ConfigureCamera(Camera targetCamera, int cloudLayer)
    {
        if (targetCamera == null)
            return;

        float[] distances = targetCamera.layerCullDistances;

        if (distances == null || distances.Length != 32)
            return;

        if (!originalCullingDistances.ContainsKey(targetCamera))
            originalCullingDistances.Add(targetCamera, distances[cloudLayer]);

        distances[cloudLayer] = cloudCullDistance;
        targetCamera.layerCullDistances = distances;
    }

    private void RestoreCloudDistanceCulling()
    {
        if (configuredCloudLayer < 0)
            return;

        foreach (KeyValuePair<Camera, float> pair in originalCullingDistances)
        {
            Camera targetCamera = pair.Key;

            if (targetCamera == null)
                continue;

            float[] distances = targetCamera.layerCullDistances;

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
        maxInitialPlacementsPerFrame = Mathf.Max(1, maxInitialPlacementsPerFrame);
        maxStalledFrames = Mathf.Max(1, maxStalledFrames);
        cloudCullDistance = Mathf.Max(1f, cloudCullDistance);

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
        is2D = true;
        SetCloudHeight(height2D);
    }

    public void Set3DMode()
    {
        is2D = false;
        SetCloudHeight(height3D);
    }

    private void SetCloudHeight(float height)
    {
        for (int i = 0; i < activeClouds.Count; i++)
        {
            GameObject cloud = activeClouds[i];

            if (cloud == null)
                continue;

            Vector3 position = cloud.transform.position;
            position.y = height;
            cloud.transform.position = position;
        }
    }
}
