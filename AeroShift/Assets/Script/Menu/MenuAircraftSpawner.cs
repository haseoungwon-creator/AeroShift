using UnityEngine;

public sealed class MenuAircraftSpawner : MonoBehaviour
{
    [SerializeField] private SkinManager skinManager;
    [SerializeField] private Transform spawnPoint;

    private GameObject spawnedAircraft;

    public GameObject SpawnedAircraft => spawnedAircraft;

    private void Start()
    {
        ShowCurrentAircraft();
    }

    // 마지막으로 확정한 비행기를 표시한다.
    public void ShowCurrentAircraft()
    {
        if (skinManager == null)
        {
            Debug.LogWarning("[MenuAircraftSpawner] SkinManager가 연결되지 않았습니다.", this);
            return;
        }

        SpawnVisual(skinManager.CurrentSkin);
    }

    // 아직 확정하지 않은 선택 후보를 미리 보여준다.
    public void ShowSelectedAircraft()
    {
        if (skinManager == null)
            return;

        SpawnVisual(skinManager.SelectedSkin);
    }

    // 나중에 비행기 선택 UI의 좌우 버튼에서 호출한다.
    public void NextAircraft()
    {
        if (skinManager == null)
            return;

        skinManager.SelectNext();
        ShowSelectedAircraft();
    }

    public void PreviousAircraft()
    {
        if (skinManager == null)
            return;

        skinManager.SelectPrevious();
        ShowSelectedAircraft();
    }

    public void ConfirmAircraft()
    {
        if (skinManager == null)
            return;

        skinManager.ConfirmSkin();
        ShowCurrentAircraft();
    }

    public void CancelAircraft()
    {
        if (skinManager == null)
            return;

        skinManager.ResetSelection();
        ShowCurrentAircraft();
    }

    private void SpawnVisual(SkinData data)
    {
        if (data == null || data.AircraftPrefab == null)
        {
            Debug.LogWarning("[MenuAircraftSpawner] 선택한 SkinData에 Aircraft Prefab이 없습니다.", this);
            return;
        }

        if (spawnedAircraft != null)
            Destroy(spawnedAircraft);

        Transform anchor = spawnPoint != null ? spawnPoint : transform;
        spawnedAircraft = Instantiate(
            data.AircraftPrefab,
            anchor.position,
            anchor.rotation,
            anchor);
    }
}
