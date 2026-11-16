using UnityEngine;
using UnityEngine.UI;

public class AircraftSkinUI : MonoBehaviour
{
    [SerializeField] private AircraftSpawner aircraftSpawner;
    [SerializeField] private MenuButtonController menuButtonController;
    [SerializeField] private Text aircraftNameText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Text passiveText;

    private AircraftJsonData aircraftData;
    private int previewIndex;

    private void OnEnable()
    {
        if (aircraftSpawner == null || aircraftSpawner.SelectedAircraft == null)
            return;

        TextAsset json = Resources.Load<TextAsset>("Config/AircraftSkins");

        if (json == null)
            return;

        aircraftData = JsonUtility.FromJson<AircraftJsonData>(json.text);

        if (aircraftData == null || aircraftData.aircraft == null || aircraftData.aircraft.Length == 0)
            return;

        string selectedId = aircraftSpawner.SelectedAircraft.AircraftId;

        for (int i = 0; i < aircraftData.aircraft.Length; i++)
        {
            AircraftInfo info = aircraftData.aircraft[i];

            if (info != null && info.id == selectedId)
            {
                ShowAircraft(i);
                return;
            }
        }

        ShowAircraft(0);
    }

    private void OnDisable()
    {
        // Exit 애니메이션이 UI를 끄는 경우에도 미확정 변경을 취소
        CancelPreview();
    }

    public void Previous()
    {
        if (aircraftData == null || aircraftData.aircraft == null)
            return;

        int index = previewIndex - 1;

        if (index < 0)
            index = aircraftData.aircraft.Length - 1;

        ShowAircraft(index);
    }

    public void Next()
    {
        if (aircraftData == null || aircraftData.aircraft == null)
            return;

        int index = previewIndex + 1;

        if (index >= aircraftData.aircraft.Length)
            index = 0;

        ShowAircraft(index);
    }

    public void Select()
    {
        if (aircraftData == null || aircraftSpawner == null)
            return;

        if (aircraftData.aircraft == null || previewIndex >= aircraftData.aircraft.Length)
            return;

        AircraftInfo info = aircraftData.aircraft[previewIndex];

        if (info == null || aircraftSpawner.SelectedAircraft == null)
            return;

        // SELECT에서만 SO와 PlayerPrefs 변경
        aircraftSpawner.SelectedAircraft.SetAircraft(info);
        aircraftSpawner.SelectedAircraft.Save();
        aircraftSpawner.ApplySelected();

        if (menuButtonController != null)
            menuButtonController.Back();
    }

    public void Back()
    {
        CancelPreview();

        if (menuButtonController != null)
            menuButtonController.Back();
    }

    private void CancelPreview()
    {
        if (aircraftSpawner == null)
            return;

        aircraftSpawner.ApplySelected();
    }

    private void ShowAircraft(int index)
    {
        if (aircraftData == null || aircraftData.aircraft == null)
            return;

        if (index < 0 || index >= aircraftData.aircraft.Length)
            return;

        AircraftInfo info = aircraftData.aircraft[index];

        if (info == null || aircraftSpawner == null)
            return;

        if (!aircraftSpawner.ChangeMesh(info.id))
            return;

        previewIndex = index;

        if (aircraftNameText != null)
            aircraftNameText.text = info.displayName;

        if (descriptionText != null)
            descriptionText.text = info.description;

        if (passiveText != null)
        {
            if (info.passive == null)
                passiveText.text = "패시브 없음";
            else
                passiveText.text = info.passive.displayName + " : " + info.passive.description;
        }
    }
}
