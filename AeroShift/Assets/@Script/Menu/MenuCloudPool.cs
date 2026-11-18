using System.Collections.Generic;
using UnityEngine;

public sealed class MenuCloudPool : MonoBehaviour
{
    private GameObject[] prefabs;
    private Stack<Transform>[] available;
    private Transform cloudParent;
    private bool initialized;

    public void Initialize(GameObject[] cloudPrefabs, Transform parent)
    {
        if (initialized)
            return;

        prefabs = cloudPrefabs;
        cloudParent = parent;

        int count = prefabs != null ? prefabs.Length : 0;
        available = new Stack<Transform>[count];

        for (int i = 0; i < count; i++)
            available[i] = new Stack<Transform>();

        initialized = true;
    }

    public Transform Rent(int prefabIndex, Vector3 localPosition, Quaternion localRotation, float scale)
    {
        if (!initialized || prefabIndex < 0 || prefabIndex >= available.Length || prefabs[prefabIndex] == null)
            return null;

        Transform cloud;
        Stack<Transform> pool = available[prefabIndex];

        if (pool.Count > 0)
        {
            cloud = pool.Pop();
            cloud.localPosition = localPosition;
            cloud.localRotation = localRotation;
            cloud.localScale = prefabs[prefabIndex].transform.localScale * scale;
            cloud.gameObject.SetActive(true);
        }
        else
        {

            GameObject instance = Instantiate(
                prefabs[prefabIndex],
                cloudParent.TransformPoint(localPosition),
                cloudParent.rotation * localRotation,
                cloudParent);

            cloud = instance.transform;
            cloud.localScale = prefabs[prefabIndex].transform.localScale * scale;
        }

        return cloud;
    }

    public void Return(int prefabIndex, Transform cloud)
    {
        if (!initialized || cloud == null || prefabIndex < 0 || prefabIndex >= available.Length)
            return;

        cloud.gameObject.SetActive(false);
        available[prefabIndex].Push(cloud);
    }
}
