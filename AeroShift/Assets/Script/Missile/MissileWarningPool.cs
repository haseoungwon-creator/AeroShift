using System.Collections.Generic;
using UnityEngine;

public class MissileWarningPool : MonoBehaviour
{
    [SerializeField] GameObject warningPrefab;
    [SerializeField] int initialPoolSize = 40;

    private readonly Queue<GameObject> warningPool = new Queue<GameObject>();
    private readonly HashSet<GameObject> activeWarnings = new HashSet<GameObject>();

    private void Awake()
    {
        CreatePool();
    }

    private void CreatePool()
    {
        if (warningPrefab == null) return;

        for(int i = 0; i < initialPoolSize; i++)
        {
            GameObject warning = Instantiate(warningPrefab, transform);
            warning.SetActive(false);
            warningPool.Enqueue(warning);
        }
    }

    public GameObject GetWarning()
    {
        if(warningPool.Count == 0) return null;

        GameObject warning = warningPool.Dequeue();
        warning.SetActive(true);
        activeWarnings.Add(warning);
        return warning;
    }

    public void ReturnWarning(GameObject warning)
    {
        if(warning == null) return;
        if (!activeWarnings.Remove(warning)) return;

        warning.SetActive(false);
        warningPool.Enqueue(warning);
    }

    public void ReturnAllWarnings()
    {
        foreach (GameObject warning in activeWarnings)
        {
            if(warning != null)
            {
                warning.SetActive(false);
                warningPool.Enqueue(warning);
            }
        }
        activeWarnings.Clear();
    }
}
