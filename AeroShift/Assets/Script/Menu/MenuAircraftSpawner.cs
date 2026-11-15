using UnityEngine;

// 스킨 선택 정보와 독립적으로, 메뉴에 보여줄 외형 모델 한 대만 관리한다.
public sealed class MenuAircraftSpawner: MonoBehaviour
{
    [SerializeField] private SkinManager skinManager;
    [SerializeField] private Transform spawnPoint;

    private GameObject spawnedAircraft;
    private GameObject lastPrefab;

    public GameObject SpawnedAircraft => spawnedAircraft;

    private void Start()
    {
        ShowCurrentAircraft();
    }

    public void ShowCurrentAircraft()
    {
        if (skinManager != null)
            SpawnVisual(skinManager.CurrentAircraftPrefab);
    }

    public void ShowSelectedAircraft()
    {
        if (skinManager != null)
            SpawnVisual(skinManager.SelectedAircraftPrefab);
    }

    public void NextAircraft()
    {
        if (skinManager == null) return;
        skinManager.SelectNext();
        ShowSelectedAircraft();
    }

    public void PreviousAircraft()
    {
        if (skinManager == null) return;
        skinManager.SelectPrevious();
        ShowSelectedAircraft();
    }

    public void ConfirmAircraft()
    {
        if (skinManager == null) return;
        if (skinManager.TryConfirmSkin())
            ShowCurrentAircraft();
    }

    public void CancelAircraft()
    {
        if (skinManager == null) return;
        skinManager.ResetSelection();
        ShowCurrentAircraft();
    }

    private void SpawnVisual(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[MenuAircraftSpawner] 스킨에 연결한 비행기 프리팹이 없습니다.", this);
            return;
        }
        if (spawnedAircraft != null && prefab == lastPrefab)
            return;

        if (spawnedAircraft != null)
        {
            spawnedAircraft.SetActive(false);
            Destroy(spawnedAircraft);
        }

        Transform anchor = spawnPoint != null ? spawnPoint : transform;
        spawnedAircraft = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
        lastPrefab = prefab;
    }
}
