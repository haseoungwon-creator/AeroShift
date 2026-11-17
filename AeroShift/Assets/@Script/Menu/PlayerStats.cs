using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private SelectedAircraftSO selectedAircraft;

    public string AircraftId { get; private set; }
    public float ScoreMultiplier { get; private set; } = 1f;
    public float WorldSpeedMultiplier { get; private set; } = 1f;
    public int BonusSkillRerolls { get; private set; }

    private void Start()
    {
        LoadStats();
    }

    public void LoadStats()
    {
        ScoreMultiplier = 1f;
        WorldSpeedMultiplier = 1f;
        BonusSkillRerolls = 0;
        AircraftId = "";

        if (selectedAircraft == null)
        {
            Debug.LogWarning("[PlayerStats] SelectedAircraftSO가 연결되지 않았습니다.", this);
            return;
        }

        AircraftId = selectedAircraft.AircraftId;

        if (selectedAircraft.PassiveId == "score_multiplier")
        {
            ScoreMultiplier = selectedAircraft.PassiveMultiplier;
        }
        else if (selectedAircraft.PassiveId == "world_speed_multiplier")
        {
            WorldSpeedMultiplier = selectedAircraft.PassiveMultiplier;
        }
        else if (selectedAircraft.PassiveId == "skill_reroll_bonus")
        {
            BonusSkillRerolls = selectedAircraft.BonusRerolls;
        }
    }
}
