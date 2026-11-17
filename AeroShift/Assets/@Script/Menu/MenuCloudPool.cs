using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메인 메뉴 구름 전용 Object Pool.
/// 메뉴 이외의 PoolManager와는 독립적으로 동작합니다.
/// 구름이 화면 뒤로 지나가면 Destroy 대신 비활성화하여 보관하고 다시 사용합니다.
/// </summary>
[DisallowMultipleComponent]
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
            // 처음 필요할 때만 생성합니다. 이후에는 반환된 구름을 재사용합니다.
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
