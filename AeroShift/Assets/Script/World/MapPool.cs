using System.Collections.Generic;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;

public class MapPool : MonoBehaviour
{
    [SerializeField] GameObject safeMapPrefab;
    [SerializeField] GameObject[] mapPrefabs;
    [SerializeField] int initialPoolSize;
    public int MapPrefabsCount => mapPrefabs.Length;

    private readonly Queue<GameObject> safeMapPool = new Queue<GameObject>();
    private readonly List<Queue<GameObject>> mapPools = new List<Queue<GameObject>>();

    private void Awake()
    {
        CreateSafeMapPool();
        CreateMapPool();
    }

    private void CreateSafeMapPool()
    {
        if (safeMapPrefab == null)
            return;

        for (int i = 0;i < initialPoolSize; i++)
        {
            GameObject map = Instantiate(safeMapPrefab, transform);
            map.SetActive(false);
            safeMapPool.Enqueue(map);
        }
    }

    private void CreateMapPool()
    {
        if (mapPrefabs == null || mapPrefabs.Length == 0)
            return;

        for(int i = 0; i < mapPrefabs.Length; i++)
        {
            Queue<GameObject> pool = new Queue<GameObject>();
            mapPools.Add(pool);

            if (mapPrefabs[i] == null)
                continue;

            for(int j = 0; j < initialPoolSize; j++)
            {
                GameObject map = Instantiate(mapPrefabs[i], transform);
                map.SetActive(false);
                pool.Enqueue(map);
            }
        }
    }

    public GameObject GetSafeMap()
    {
        if(safeMapPool.Count == 0) return null;

        GameObject map = safeMapPool.Dequeue();
        map.SetActive(true);
        return map;
    }

    public GameObject GetMap(int mapIndex)
    {
        if(mapIndex < 0 || mapIndex >= mapPools.Count)
            return null;

        Queue<GameObject> pool = mapPools[mapIndex];

        if(pool.Count == 0)
            return null;

        GameObject map = pool.Dequeue();
        map.SetActive(true);
        return map;
    }

    public void ReturnSafeMap(GameObject map)
    {
        if(map == null) return;

        map.SetActive(false);
        safeMapPool.Enqueue(map);
    }

    public void ReturnMap(int mapIndex,GameObject map)
    {
        if (map == null || mapIndex < 0 || mapIndex >= mapPools.Count) return;

        map.SetActive(false);
        mapPools[mapIndex].Enqueue(map);
    }
}
