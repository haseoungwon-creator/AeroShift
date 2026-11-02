using UnityEngine;

public class CloudPool : MonoBehaviour
{
    [SerializeField] GameObject[] cloudPrefabs;
    [SerializeField] int poolSize = 800;

    private GameObject[] clouds;

    public int PoolSize => poolSize;

    private void Awake()
    {
        CreatePool();
    }

    private void CreatePool()
    {
        if (cloudPrefabs == null || cloudPrefabs.Length == 0)
            return;

        clouds = new GameObject[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            GameObject prefab =
                cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];

            GameObject cloud =
                Instantiate(prefab, transform);

            cloud.SetActive(false);
            clouds[i] = cloud;
        }
    }

    public GameObject GetCloud()
    {
        if (clouds == null)
            return null;

        for (int i = 0; i < clouds.Length; i++)
        {
            if (!clouds[i].activeSelf)
            {
                clouds[i].SetActive(true);
                return clouds[i];
            }
        }

        return null;
    }

    public void ReturnCloud(GameObject cloud)
    {
        if (cloud == null)
            return;

        cloud.SetActive(false);
    }
}