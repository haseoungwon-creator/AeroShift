using UnityEngine;

[CreateAssetMenu(fileName = "SelectedAircraft", menuName = "AeroShift/Selected Aircraft")]
public class SelectedAircraftSO : ScriptableObject
{
    [SerializeField] private string aircraftId = "aircraft-a";
    [SerializeField] private string aircraftName;
    [SerializeField] private string description;
    [SerializeField] private string passiveId = "none";
    [SerializeField] private string passiveName;
    [SerializeField] private string passiveDescription;
    [SerializeField] private float passiveMultiplier = 1f;
    [SerializeField] private int bonusRerolls;

    private const string SaveKey = "AeroShift.SelectedAircraftId";

    public string AircraftId { get { return aircraftId; } }
    public string AircraftName { get { return aircraftName; } }
    public string Description { get { return description; } }
    public string PassiveId { get { return passiveId; } }
    public string PassiveName { get { return passiveName; } }
    public string PassiveDescription { get { return passiveDescription; } }
    public float PassiveMultiplier { get { return passiveMultiplier; } }
    public int BonusRerolls { get { return bonusRerolls; } }

    public void SetAircraft(AircraftInfo info)
    {
        if (info == null)
            return;

        aircraftId = info.id;
        aircraftName = info.displayName;
        description = info.description;

        passiveId = "none";
        passiveName = "기본형";
        passiveDescription = "추가 효과 없음";
        passiveMultiplier = 1f;
        bonusRerolls = 0;

        if (info.passive == null)
            return;

        passiveId = info.passive.id;
        passiveName = info.passive.displayName;
        passiveDescription = info.passive.description;
        passiveMultiplier = info.passive.multiplier;
        bonusRerolls = info.passive.bonusRerolls;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(aircraftId))
            return;

        PlayerPrefs.SetString(SaveKey, aircraftId);
        PlayerPrefs.Save();
    }

    public void LoadSaved()
    {
        TextAsset json = Resources.Load<TextAsset>("Config/AircraftSkins");

        if (json == null)
        {
            Debug.LogWarning("AircraftSkins.json을 찾을 수 없습니다.");
            return;
        }

        AircraftJsonData data = JsonUtility.FromJson<AircraftJsonData>(json.text);

        if (data == null || data.aircraft == null || data.aircraft.Length == 0)
            return;

        string savedId = PlayerPrefs.GetString(SaveKey, "aircraft-a");

        for (int i = 0; i < data.aircraft.Length; i++)
        {
            if (data.aircraft[i] == null)
                continue;

            if (data.aircraft[i].id == savedId)
            {
                SetAircraft(data.aircraft[i]);
                return;
            }
        }

        for (int i = 0; i < data.aircraft.Length; i++)
        {
            if (data.aircraft[i] != null)
            {
                SetAircraft(data.aircraft[i]);
                return;
            }
        }
    }
}
