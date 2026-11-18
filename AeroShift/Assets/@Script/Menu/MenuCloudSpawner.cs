using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class MenuCloudSpawner : MonoBehaviour
{
    [SerializeField] private GameObject[] cloudPrefabs;

    [SerializeField] private int initialCloudCount = 890;

    [SerializeField] private int cloudsPerWave = 9;
    [SerializeField] private float spawnInterval = 0.05f;
    [SerializeField] private Vector2 horizontalRange = new Vector2(-500f, 500f);
    [SerializeField] private Vector2 heightRange = new Vector2(-30f, -30f);
    [SerializeField] private float spawnZ = 1144.6f;
    [SerializeField] private float despawnZ = -190.9f;

    [SerializeField] private float cloudSpeed = 182.34f;
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.5f);
    [SerializeField] private bool randomizeYRotation = true;

    private struct ActiveCloud
    {
        public Transform Transform;
        public int PrefabIndex;
    }

    private readonly List<ActiveCloud> activeClouds = new List<ActiveCloud>(1500);
    private readonly List<int> validPrefabIndices = new List<int>(8);
    private MenuCloudPool cloudPool;
    private Transform movingCloudRoot;
    private Camera menuCamera;
    private float spawnTimer;
    private bool initialCloudsReady;

    private void Awake()
    {
        cloudPool = GetComponent<MenuCloudPool>();
        menuCamera = Camera.main;

        if (cloudPrefabs != null)
        {
            for (int i = 0; i < cloudPrefabs.Length; i++)
            {
                if (cloudPrefabs[i] != null)
                    validPrefabIndices.Add(i);
            }
        }

        if (cloudPool == null || validPrefabIndices.Count == 0 || initialCloudCount < 0 || cloudsPerWave < 0 ||
            spawnInterval <= 0f || cloudSpeed < 0f || spawnZ <= despawnZ)
        {
            enabled = false;
            return;
        }

        GameObject cloudRoot = new GameObject("MovingClouds");
        movingCloudRoot = cloudRoot.transform;
        movingCloudRoot.SetParent(transform, false);
        cloudPool.Initialize(cloudPrefabs, movingCloudRoot);
    }

    private IEnumerator Start()
    {
        if (!enabled)
            yield break;

        yield return FillInitialClouds();
        spawnTimer = 0f;
        initialCloudsReady = true;
    }

    private IEnumerator FillInitialClouds()
    {
        if (initialCloudCount == 0)
            yield break;

        float depth = spawnZ - despawnZ;
        float travelPerWave = cloudSpeed * spawnInterval;
        int bands = travelPerWave > 0.001f
            ? Mathf.CeilToInt(depth / travelPerWave)
            : 1;
        bands = Mathf.Clamp(bands, 1, initialCloudCount);

        int baseCount = initialCloudCount / bands;
        int remainder = initialCloudCount % bands;
        float bandDepth = depth / bands;

        int createdThisFrame = 0;

        for (int band = 0; band < bands; band++)
        {
            int remaining = baseCount + (band < remainder ? 1 : 0);
            float centerZ = despawnZ + (band + 0.5f) * bandDepth;
            float z = centerZ + Random.Range(-0.2f, 0.2f) * bandDepth;
            z = Mathf.Clamp(z, despawnZ + 0.001f, spawnZ - 0.001f);

            while (remaining > 0)
            {
                int batch = Mathf.Min(remaining, 16 - createdThisFrame);
                SpawnWave(batch, z, bandDepth);
                remaining -= batch;
                createdThisFrame += batch;

                if (createdThisFrame >= 16)
                {
                    createdThisFrame = 0;
                    yield return null;
                }
            }
        }
    }

    private void Update()
    {
        if (!initialCloudsReady)
            return;

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float moveDistance = cloudSpeed * deltaTime;

        movingCloudRoot.localPosition += Vector3.back * moveDistance;
        RebaseCloudRoot();

        float rootZ = movingCloudRoot.localPosition.z;
        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            ActiveCloud item = activeClouds[i];
            Transform cloud = item.Transform;

            if (cloud == null)
            {
                RemoveActiveAt(i);
                continue;
            }

            if (cloud.localPosition.z + rootZ <= despawnZ)
            {
                cloudPool.Return(item.PrefabIndex, cloud);
                RemoveActiveAt(i);
            }
        }

        spawnTimer += deltaTime;
        int waveCount = Mathf.FloorToInt(spawnTimer / spawnInterval);
        if (waveCount <= 0)
            return;

        spawnTimer -= waveCount * spawnInterval;

        for (int wave = waveCount - 1; wave >= 0; wave--)
        {
            float elapsedSinceSpawn = spawnTimer + wave * spawnInterval;
            float z = spawnZ - cloudSpeed * elapsedSinceSpawn;
            SpawnWave(cloudsPerWave, z, cloudSpeed * spawnInterval);
        }
    }

    private void SpawnWave(int count, float localZ, float waveDepth)
    {
        if (count <= 0)
            return;

        GetVisibleHorizontalRange(localZ, out float minX, out float maxX);

        float zSpread = Mathf.Max(0f, waveDepth) * 0.45f;
        for (int i = 0; i < count; i++)
        {
            float x = Random.Range(minX, maxX);
            float z = localZ + Random.Range(-zSpread, zSpread);
            z = Mathf.Clamp(z, despawnZ + 0.001f, spawnZ);
            SpawnCloud(x, z);
        }
    }

    private void GetVisibleHorizontalRange(float localZ, out float minX, out float maxX)
    {
        minX = Mathf.Min(horizontalRange.x, horizontalRange.y);
        maxX = Mathf.Max(horizontalRange.x, horizontalRange.y);

        if (menuCamera == null)
            return;

        Plane zPlane = new Plane(transform.forward, transform.TransformPoint(0f, 0f, localZ));
        Ray leftRay = menuCamera.ViewportPointToRay(new Vector3(0f, 0.5f, 0f));
        Ray rightRay = menuCamera.ViewportPointToRay(new Vector3(1f, 0.5f, 0f));

        if (!zPlane.Raycast(leftRay, out float leftDistance) ||
            !zPlane.Raycast(rightRay, out float rightDistance) ||
            leftDistance <= 0f || rightDistance <= 0f)
            return;

        float leftX = transform.InverseTransformPoint(leftRay.GetPoint(leftDistance)).x;
        float rightX = transform.InverseTransformPoint(rightRay.GetPoint(rightDistance)).x;
        float centerX = (leftX + rightX) * 0.5f;
        float halfWidth = Mathf.Max(Mathf.Abs(rightX - leftX) * 0.55f, 10f);

        float visibleMin = Mathf.Max(minX, centerX - halfWidth);
        float visibleMax = Mathf.Min(maxX, centerX + halfWidth);

        if (visibleMax > visibleMin)
        {
            minX = visibleMin;
            maxX = visibleMax;
        }
    }

    private void SpawnCloud(float localX, float localZ)
    {
        int index = validPrefabIndices[Random.Range(0, validPrefabIndices.Count)];
        float y = Random.Range(heightRange.x, heightRange.y);
        float scale = Random.Range(scaleRange.x, scaleRange.y);
        Quaternion rotation = randomizeYRotation
            ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            : Quaternion.identity;

        Vector3 position = new Vector3(localX, y, localZ - movingCloudRoot.localPosition.z);
        Transform cloud = cloudPool.Rent(index, position, rotation, scale);
        if (cloud != null)
            activeClouds.Add(new ActiveCloud { Transform = cloud, PrefabIndex = index });
    }

    private void RebaseCloudRoot()
    {
        float offset = movingCloudRoot.localPosition.z;
        if (offset > -10000f)
            return;

        for (int i = 0; i < activeClouds.Count; i++)
        {
            Transform cloud = activeClouds[i].Transform;
            if (cloud == null)
                continue;

            Vector3 position = cloud.localPosition;
            position.z += offset;
            cloud.localPosition = position;
        }

        movingCloudRoot.localPosition = Vector3.zero;
    }

    private void RemoveActiveAt(int index)
    {
        int last = activeClouds.Count - 1;
        activeClouds[index] = activeClouds[last];
        activeClouds.RemoveAt(last);
    }
}
