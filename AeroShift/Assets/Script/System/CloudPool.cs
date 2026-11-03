using System.Collections.Generic;
using UnityEngine;

public class CloudPool : MonoBehaviour
{
    [SerializeField] private GameObject[] cloudPrefabs;
    [SerializeField, Min(0)] private int poolSize = 800;

    private Queue<GameObject> availableClouds;
    private HashSet<GameObject> rentedClouds;

    public int PoolSize => poolSize;

    public int AvailableCount => availableClouds?.Count ?? 0;
    public int ActiveCount => rentedClouds?.Count ?? 0;

    private void Awake()
    {
        CreatePool();
    }

    private void CreatePool()
    {
        if (poolSize <= 0 || cloudPrefabs == null || cloudPrefabs.Length == 0)
        {
            Debug.LogError("CloudPool: Pool size or cloud prefabs are not configured.", this);
            return;
        }

        var validPrefabs = new List<GameObject>(cloudPrefabs.Length);
        foreach (GameObject prefab in cloudPrefabs)
        {
            if (prefab != null)
                validPrefabs.Add(prefab);
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogError("CloudPool: No valid cloud prefabs were assigned.", this);
            return;
        }

        availableClouds = new Queue<GameObject>(poolSize);
        rentedClouds = new HashSet<GameObject>();

        for (int i = 0; i < poolSize; i++)
        {
            GameObject prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            GameObject cloud = Instantiate(prefab, transform);
            cloud.SetActive(false);
            availableClouds.Enqueue(cloud);
        }
    }

    public GameObject GetCloud()
    {
        if (availableClouds == null || availableClouds.Count == 0)
            return null;

        GameObject cloud = availableClouds.Dequeue();
        rentedClouds.Add(cloud);
        cloud.SetActive(true);
        return cloud;
    }

    public void ReturnCloud(GameObject cloud)
    {
        if (cloud == null || rentedClouds == null)
            return;

        if (!rentedClouds.Remove(cloud))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("CloudPool: Ignored duplicate return or a cloud from another pool.", this);
#endif
            return;
        }

        cloud.SetActive(false);
        availableClouds.Enqueue(cloud);
    }
}
