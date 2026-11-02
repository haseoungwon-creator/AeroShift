using System.Collections.Generic;
using UnityEngine;

public class MissilePool : MonoBehaviour
{
    [SerializeField] GameObject missilePrefab;
    [SerializeField] int initialPoolSize = 30;

    private readonly Queue<GameObject> missilePool = new Queue<GameObject>();
    private readonly HashSet<GameObject> activeMissiles = new HashSet<GameObject>();
    private void Awake()
    {
        CreatePool();
    }

    private void CreatePool()
    {
        if (missilePrefab == null) return;

        for(int i = 0; i < initialPoolSize; i++)
        {
            GameObject missile = Instantiate(missilePrefab,transform);
            missile.SetActive(false);
            missilePool.Enqueue(missile);
        }
    }

    public GameObject GetMissile()
    {
        if(missilePool.Count == 0) return null;

        GameObject missile = missilePool.Dequeue();
        missile.SetActive(true);
        activeMissiles.Add(missile);

        return missile;
    }

    public void ReturnMissile(GameObject missile)
    {
        if(missile == null) return;
        if(!activeMissiles.Remove(missile)) return;

        missile.SetActive(false);
        missilePool.Enqueue(missile);
    }

    public void ReturnAllMissiles()
    {
        foreach(GameObject missile in activeMissiles)
        {
            if (missile == null) continue;

            missile.SetActive(false);
            missilePool.Enqueue(missile);
        }
        activeMissiles.Clear();
    }

    public bool HasActiveMissiles()
    {
        return activeMissiles.Count > 0;
    }
}
