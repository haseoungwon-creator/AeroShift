using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메인 메뉴 구름 생성·초기 배치·일괄 이동 담당.
/// 초기 구름과 이후 생성 구름의 Z 간격은 유지하면서, X 위치의 반복 패턴을 없앱니다.
/// 개별 구름에는 Update가 필요하지 않으며, 반환은 MenuCloudPool이 담당합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MenuCloudPool))]
public sealed class MenuCloudSpawner : MonoBehaviour
{
    [Header("Cloud Prefabs")]
    [SerializeField] private GameObject[] cloudPrefabs;

    [Header("Initial Clouds")]
    [SerializeField] private int initialCloudCount = 890;

    [Header("Spawn")]
    [SerializeField] private int cloudsPerWave = 9;
    [SerializeField] private float spawnInterval = 0.05f;
    [SerializeField] private Vector2 horizontalRange = new Vector2(-500f, 500f);
    [SerializeField] private Vector2 heightRange = new Vector2(-30f, -30f);
    [SerializeField] private float spawnZ = 1144.6f;
    [SerializeField] private float despawnZ = -190.9f;

    [Header("Movement & Look")]
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
    private Camera menuCamera;
    private float spawnTimer;

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

        if (validPrefabIndices.Count == 0 || initialCloudCount < 0 || cloudsPerWave < 0 ||
            spawnInterval <= 0f || cloudSpeed < 0f || spawnZ <= despawnZ)
        {
            Debug.LogWarning("[MenuCloudSpawner] 구름 Prefab 또는 생성/이동 설정을 확인하세요.", this);
            enabled = false;
            return;
        }

        cloudPool.Initialize(cloudPrefabs, transform);
    }

    private void Start()
    {
        if (!enabled)
            return;

        FillInitialClouds();
        spawnTimer = 0f;
    }

    private void FillInitialClouds()
    {
        if (initialCloudCount == 0)
            return;

        // 실행 중 구름이 유입되는 간격에 맞춰 Z 방향의 띠를 나눕니다.
        // 이전 방식은 띠가 너무 적어 초기 구름 사이에 큰 빈 공간이 생길 수 있었습니다.
        float depth = spawnZ - despawnZ;
        float travelPerWave = cloudSpeed * spawnInterval;
        int bands = travelPerWave > 0.001f
            ? Mathf.CeilToInt(depth / travelPerWave)
            : 1;
        bands = Mathf.Clamp(bands, 1, initialCloudCount);

        int baseCount = initialCloudCount / bands;
        int remainder = initialCloudCount % bands;
        float bandDepth = depth / bands;

        for (int band = 0; band < bands; band++)
        {
            int count = baseCount + (band < remainder ? 1 : 0);
            float centerZ = despawnZ + (band + 0.5f) * bandDepth;
            float z = centerZ + Random.Range(-0.2f, 0.2f) * bandDepth;
            z = Mathf.Clamp(z, despawnZ + 0.001f, spawnZ - 0.001f);

            SpawnWave(count, z, bandDepth);
        }
    }

    private void Update()
    {
        // 메뉴 연출이므로 멈칫한 프레임 이후에도 구름이 한꺼번에 멀리 이동해
        // 큰 빈 구간이 생기지 않도록 한 프레임의 시뮬레이션 시간을 제한합니다.
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float moveDistance = cloudSpeed * deltaTime;

        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            ActiveCloud item = activeClouds[i];
            Transform cloud = item.Transform;

            if (cloud == null)
            {
                RemoveActiveAt(i);
                continue;
            }

            Vector3 position = cloud.localPosition;
            position.z -= moveDistance;
            cloud.localPosition = position;

            if (position.z <= despawnZ)
            {
                cloudPool.Return(item.PrefabIndex, cloud);
                RemoveActiveAt(i);
            }
        }

        // 프레임이 조금 늦어져도 생성 파동을 생략하지 않습니다.
        // 누락된 파동의 위치를 Z축으로 보정해 일정 간격으로 이어 붙입니다.
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

        // 기존의 'X축을 count개의 칸으로 나눠 각 칸에 1개씩 배치'하는
        // 방식은 Z 방향으로 비슷한 X 위치가 반복되어 줄무늬처럼 보였습니다.
        // 이제 각 구름의 X 위치를 독립적으로 무작위 추첨합니다.
        // Z도 한 생성 파동의 간격 안에서 살짝 분산해 일직선 배열을 피합니다.
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

        // 카메라가 지정되지 않았거나 해당 Z 평면이 카메라 뒤쪽이면
        // 기존 Inspector의 전체 Horizontal Range를 그대로 사용합니다.
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

        Transform cloud = cloudPool.Rent(index, new Vector3(localX, y, localZ), rotation, scale);
        if (cloud != null)
            activeClouds.Add(new ActiveCloud { Transform = cloud, PrefabIndex = index });
    }

    private void RemoveActiveAt(int index)
    {
        int last = activeClouds.Count - 1;
        activeClouds[index] = activeClouds[last];
        activeClouds.RemoveAt(last);
    }
}
