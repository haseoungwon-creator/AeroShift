using System.Collections.Generic;
using UnityEngine;

public class CloudSpawner : MonoBehaviour
{
    [SerializeField] private PoolManager poolManager;
    [SerializeField] private float spawnWidth = 200f;
    [SerializeField] private float spawnDistance = 1400f;
    [SerializeField] private float spawnInterval = 0.1f;
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float despawnDistance = 70f;
    [SerializeField] private float height3D = 80f;
    [SerializeField] private float height2D = -20f;
    [SerializeField] private WorldMovement worldMovement;

    private readonly List<GameObject> activeClouds = new List<GameObject>(800);
    private float spawnTimer;
    private bool isInitialized;
    private bool is2D;

    private void Start()
    {
        InitializeClouds();
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        SpawnClouds();
        MoveClouds();
    }

    private void InitializeClouds()
    {
        if (poolManager == null || worldMovement == null || spawnInterval <= 0f || moveSpeed <= 0f)
        {
            Debug.LogError("CloudSpawner 설정을 확인하세요.", this);
            enabled = false;
            return;
        }

        float distance = spawnDistance + despawnDistance;
        int count = Mathf.CeilToInt(distance / (moveSpeed * spawnInterval));

        for (int i = 0; i < count; i++)
        {
            float z = transform.position.z + spawnDistance - moveSpeed * spawnInterval * i;
            float x = transform.position.x + Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
            Vector3 spawnPosition = new Vector3(x, GetCloudHeight(), z);

            GameObject cloud = poolManager.Rent(PoolId.Cloud, spawnPosition);

            if (cloud == null)
                break;

            activeClouds.Add(cloud);
        }

        isInitialized = true;
    }

    private void SpawnClouds()
    {
        spawnTimer += Time.deltaTime;

        if (spawnTimer < spawnInterval)
            return;

        spawnTimer -= spawnInterval;

        float x = transform.position.x + Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
        Vector3 spawnPosition = new Vector3(x, GetCloudHeight(), transform.position.z + spawnDistance);

        GameObject cloud = poolManager.Rent(PoolId.Cloud, spawnPosition);

        if (cloud != null)
            activeClouds.Add(cloud);
    }

    private void MoveClouds()
    {
        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            GameObject cloud = activeClouds[i];

            if (cloud == null)
            {
                activeClouds.RemoveAt(i);
                continue;
            }

            cloud.transform.position += Vector3.back * (moveSpeed + worldMovement.CurrentSpeed) * Time.deltaTime;

            if (cloud.transform.position.z < transform.position.z - despawnDistance)
            {
                poolManager.Return(cloud);
                activeClouds.RemoveAt(i);
            }
        }
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
