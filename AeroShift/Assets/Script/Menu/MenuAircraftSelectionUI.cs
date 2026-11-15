using UnityEngine;
using UnityEngine.UI;

// 메인 메뉴 스킨 선택창의 텍스트와 버튼 흐름만 관리한다.
// Button OnClick에 OpenSelection, Next, Previous, Select, Back을 연결한다.
public sealed class MenuAircraftSelectionUI : MonoBehaviour
{
    [SerializeField] private SkinManager skinManager;
    [SerializeField] private MenuAircraftSpawner aircraftSpawner;

    [SerializeField] private GameObject mainMenuRoot;
    [SerializeField] private GameObject selectionRoot;

    [SerializeField] private Text aircraftNameText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Text passiveText;
    [SerializeField] private Button selectButton;

    [SerializeField] private bool showSelectionOnStart;

    private void Start()
    {
        if (showSelectionOnStart)
            OpenSelection();
        else
            ShowPanels(false);
    }

    public void OpenSelection()
    {
        if (skinManager == null || aircraftSpawner == null) return;
        skinManager.ResetSelection();
        aircraftSpawner.ShowSelectedAircraft();
        ShowPanels(true);
        RefreshText();
    }

    public void Next()
    {
        if (aircraftSpawner == null) return;
        aircraftSpawner.NextAircraft();
        RefreshText();
    }

    public void Previous()
    {
        if (aircraftSpawner == null) return;
        aircraftSpawner.PreviousAircraft();
        RefreshText();
    }

    // SELECT를 눌렀을 때만 실제 사용 비행기가 변경되고 저장된다.
    public void Select()
    {
        if (skinManager == null || aircraftSpawner == null) return;
        if (!skinManager.TryConfirmSkin()) return;

        aircraftSpawner.ShowCurrentAircraft();
        ShowPanels(false);
    }

    // BACK은 미리보기만 취소한다. 저장된 선택은 변경되지 않는다.
    public void Back()
    {
        if (aircraftSpawner == null) return;
        aircraftSpawner.CancelAircraft();
        ShowPanels(false);
    }

    private void ShowPanels(bool showSelection)
    {
        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(!showSelection);
        if (selectionRoot != null)
            selectionRoot.SetActive(showSelection);
    }

    private void RefreshText()
    {
        AircraftSkinDefinition info = skinManager != null ? skinManager.SelectedInfo : null;

        if (aircraftNameText != null)
            aircraftNameText.text = info != null ? info.displayName : "비행기 데이터 없음";

        if (descriptionText != null)
            descriptionText.text = info != null ? info.description : "JSON과 프리팹 연결을 확인하세요.";

        if (passiveText != null)
        {
            AircraftPassiveDefinition passive = info != null ? info.passive : null;
            passiveText.text = passive != null
                ? "PASSIVE  |  " + passive.displayName + "\n" + passive.description
                : "PASSIVE  |  없음";
        }

        if (selectButton != null)
            selectButton.interactable = skinManager != null && skinManager.CanConfirmSelected;
    }
}
