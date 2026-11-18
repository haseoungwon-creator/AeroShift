using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [SerializeField] private SelectedAircraftSO selectedAircraft;
    [SerializeField] private SelectedSkillSO selectedSkill;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private WorldMovement worldMovement;
    [SerializeField] private PlayerMovement3D playerMovement3D;
    [SerializeField] private SkillSelectionManager skillSelectionManager;
    [SerializeField] private SkillManager skillManager;

    public string AircraftId { get; private set; }
    public float ScoreMultiplier { get; private set; } = 1f;
    public float WorldSpeedMultiplier { get; private set; } = 1f;
    public float SensitivityMultiplier { get; private set; } = 1f;
    public int BonusSkillRerolls { get; private set; }

    private void Start()
    {
        LoadStats();
    }

    public void LoadStats()
    {
        AircraftId = "";
        ScoreMultiplier = 1f;
        WorldSpeedMultiplier = 1f;
        SensitivityMultiplier = 1f;
        BonusSkillRerolls = 0;

        ApplyAircraftPassive();

        SkillInfo skill = null;

        if (selectedSkill != null)
            skill = selectedSkill.CurrentSkill;

        ApplySkillPassive(skill);

        if (scoreManager != null)
            scoreManager.SetScoreMultiplier(ScoreMultiplier);

        if (worldMovement != null)
            worldMovement.SetStatSpeedMultiplier(WorldSpeedMultiplier);

        if (playerMovement3D != null)
            playerMovement3D.SetSensitivityMultiplier(SensitivityMultiplier);

        if (skillManager != null)
            skillManager.SetSelectedSkill(skill);

        if (skillSelectionManager != null)
            skillSelectionManager.SetBonusRerolls(BonusSkillRerolls);
    }

    private void ApplyAircraftPassive()
    {
        if (selectedAircraft == null)
            return;

        AircraftId = selectedAircraft.AircraftId;

        if (selectedAircraft.PassiveId == "score_multiplier")
        {
            ScoreMultiplier *= selectedAircraft.PassiveMultiplier;
        }
        else if (selectedAircraft.PassiveId == "world_speed_multiplier")
        {
            WorldSpeedMultiplier *= selectedAircraft.PassiveMultiplier;
        }
        else if (selectedAircraft.PassiveId == "skill_reroll_bonus")
        {
            BonusSkillRerolls += selectedAircraft.BonusRerolls;
        }
    }

    private void ApplySkillPassive(SkillInfo skill)
    {
        if (skill == null)
            return;

        if (skill.id == "score_catalyst")
        {
            ScoreMultiplier *= skill.skillValue;
        }
        else if (skill.id == "speed_boost" || skill.id == "speed_dampener")
        {
            WorldSpeedMultiplier *= skill.skillValue;
        }
        else if (skill.id == "high_sensitivity" || skill.id == "low_sensitivity")
        {
            SensitivityMultiplier *= skill.skillValue;
        }
    }
}
