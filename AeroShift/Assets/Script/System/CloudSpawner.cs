using System.Collections.Generic;
using UnityEngine;

public class CloudSpawner : MonoBehaviour
{
    [SerializeField] CloudPool cloudPool;
    [SerializeField] float spawnWidth = 200f;
    [SerializeField] float spawnDistance = 1400f;
    [SerializeField] float spawnInterval = 0.1f;
    [SerializeField] float moveSpeed = 20f;
    [SerializeField] float despawnDistance = 70f;
    [SerializeField] float height3D = 80f;
    [SerializeField] float height2D = -20f;
    [SerializeField] WorldMovement worldMovement;

    private List<GameObject> activeClouds = new List<GameObject>(800);
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
        if (cloudPool == null)
            return;

        float distance =
            spawnDistance + despawnDistance;

        int count =
            Mathf.CeilToInt(
                distance /
                (moveSpeed * spawnInterval));

        for (int i = 0; i < count; i++)
        {
            GameObject cloud =
                cloudPool.GetCloud();

            if (cloud == null)
                break;

            float z =
                transform.position.z +
                spawnDistance -
                moveSpeed * spawnInterval * i;

            float x =
                transform.position.x +
                Random.Range(
                    -spawnWidth * 0.5f,
                    spawnWidth * 0.5f);

            Vector3 spawnPosition =
                new Vector3(
                    x,
                    GetCloudHeight(),
                    z);

            cloud.transform.SetPositionAndRotation(
                spawnPosition,
                cloud.transform.rotation);

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

        GameObject cloud =
            cloudPool.GetCloud();

        if (cloud == null)
            return;

        float x =
            transform.position.x +
            Random.Range(
                -spawnWidth * 0.5f,
                spawnWidth * 0.5f);

        Vector3 spawnPosition =
            new Vector3(
                x,
                GetCloudHeight(),
                transform.position.z + spawnDistance);

        cloud.transform.SetPositionAndRotation(
            spawnPosition,
            cloud.transform.rotation);

        activeClouds.Add(cloud);
    }

    private void MoveClouds()
    {
        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            GameObject cloud =
                activeClouds[i];

            if (cloud == null)
            {
                activeClouds.RemoveAt(i);
                continue;
            }
            
            cloud.transform.position +=
                Vector3.back *
                (moveSpeed + worldMovement.CurrentSpeed) *
                Time.deltaTime;

            if (cloud.transform.position.z <
                transform.position.z - despawnDistance)
            {
                cloudPool.ReturnCloud(cloud);
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
            GameObject cloud =
                activeClouds[i];

            if (cloud == null)
                continue;

            Vector3 position =
                cloud.transform.position;

            position.y = height;

            cloud.transform.position = position;
        }
    }
}