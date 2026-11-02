using System.Collections.Generic;
using UnityEngine;

public class MapManager_Unoptimized : MonoBehaviour
{
    [SerializeField] GameObject safeMapPrefab;
    [SerializeField] GameObject[] mapPrefabs;
    [SerializeField] Transform player;
    [SerializeField] float mapLength = 80f;
    [SerializeField] int forwardMapCount = 6;
    [SerializeField] int safeMapCount = 3;
    [SerializeField] float keepBehindDistance = 250f;

    [SerializeField] float moveSpeed = 30f;
    [SerializeField] float maxSpeed = 60f;
    [SerializeField] float speedIncrease = 5f;
    [SerializeField] float speedIncreaseInterval = 10f;

    private readonly Queue<GameObject> spawnedMaps = new Queue<GameObject>();

    private GameObject farthestMap;
    private int mapCount;

    private float currentSpeed;
    private float speedTimer;
    private bool isMoving;

    private void Awake()
    {
        currentSpeed = moveSpeed;
    }

    private void Start()
    {
        SpawnMapsAhead();
        isMoving = true;
    }

    private void Update()
    {
        if (!isMoving)
            return;

        MoveMaps();
        IncreaseSpeed();
        SpawnMapsAhead();
        RemoveMapsBehind();
    }

    private void MoveMaps()
    {
        float movement = currentSpeed * Time.deltaTime;

        foreach (GameObject map in spawnedMaps)
        {
            if (map == null)
                continue;

            map.transform.position += Vector3.back * movement;
        }
    }

    private void IncreaseSpeed()
    {
        speedTimer += Time.deltaTime;

        if (speedTimer < speedIncreaseInterval)
            return;

        speedTimer = 0f;

        currentSpeed = Mathf.Min(
            currentSpeed + speedIncrease,
            maxSpeed);
    }

    private void SpawnMapsAhead()
    {
        float requiredZ =
            player.position.z + mapLength * forwardMapCount;

        float farthestZ =
            farthestMap != null
                ? farthestMap.transform.position.z
                : 0f;

        int guard = 0;

        while (farthestZ < requiredZ)
        {
            float spawnZ;

            if (farthestMap == null)
                spawnZ = 0f;
            else
                spawnZ = farthestZ + mapLength;

            if (!SpawnMap(spawnZ))
                break;

            farthestZ = spawnZ;

            guard++;

            if (guard > 100)
                break;
        }
    }

    private bool SpawnMap(float spawnZ)
    {
        bool isSafe = mapCount < safeMapCount;

        GameObject prefab;

        if (isSafe)
        {
            prefab = safeMapPrefab;
        }
        else
        {
            if (mapPrefabs == null || mapPrefabs.Length == 0)
                return false;

            int index = Random.Range(0, mapPrefabs.Length);
            prefab = mapPrefabs[index];
        }

        if (prefab == null)
            return false;

        Quaternion rotation = Quaternion.identity;

        if (!isSafe)
        {
            int rotationY =
                Random.Range(0, 2) == 0 ? 0 : 180;

            rotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        GameObject map = Instantiate(
            prefab,
            new Vector3(0f, 0f, spawnZ),
            rotation);

        spawnedMaps.Enqueue(map);

        farthestMap = map;
        mapCount++;

        return true;
    }

    private void RemoveMapsBehind()
    {
        float deleteZ =
            player.position.z - keepBehindDistance;

        while (spawnedMaps.Count > 0)
        {
            GameObject map = spawnedMaps.Peek();

            if (map == null)
            {
                spawnedMaps.Dequeue();
                continue;
            }

            float frontEdge =
                map.transform.position.z + mapLength * 0.5f;

            if (frontEdge >= deleteZ)
                break;

            spawnedMaps.Dequeue();

            if (map == farthestMap)
                farthestMap = null;

            Destroy(map);
        }
    }

    public void StartMovement()
    {
        isMoving = true;
    }

    public void StopMovement()
    {
        isMoving = false;
    }

    public void ResetMaps()
    {
        while (spawnedMaps.Count > 0)
        {
            GameObject map = spawnedMaps.Dequeue();

            if (map != null)
                Destroy(map);
        }

        farthestMap = null;
        mapCount = 0;

        currentSpeed = moveSpeed;
        speedTimer = 0f;
        isMoving = false;
    }
}