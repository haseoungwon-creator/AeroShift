using UnityEditor;
using UnityEngine;

public enum SkillType
{
    None,
    time_rewind,
    slow_motion,
    missile_suppression,//
    missile_slow,//
    phase_dash,//
    shockwave,//
    phase_shield/**/
}

public class SkillManager : MonoBehaviour
{
    [SerializeField] PlayerRewind playerRewind;
    [SerializeField] TimeSlow timeSlow;
    [SerializeField] Shield shield;
    private SkillInfo selectedSkillInfo;

    public SkillType SelectedSkill { get; private set; } = SkillType.None;

    public void SetSelectedSkill(SkillInfo info)
    {
        if (info != null && selectedSkillInfo != null)
        {
            if (info.id == selectedSkillInfo.id)
                return;
        }

        if (shield != null)
            shield.ResetSkill();

        selectedSkillInfo = info;
        SelectedSkill = SkillType.None;

        if (info == null)
            return;

        if (info.id == "time_rewind")
        {
            SelectedSkill = SkillType.time_rewind;

            if (playerRewind != null)
                playerRewind.SetRewindDuration(info.skillValue);
        }
        else if (info.id == "slow_motion")
        {
            SelectedSkill = SkillType.slow_motion;
        }
        else if (info.id == "phase_shield")
        {
            SelectedSkill = SkillType.phase_shield;

            if (shield != null)
                shield.Activate();
        }
    }

    public void SelectSkill(SkillType skillType)
    {
        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Ready)
            return;

        selectedSkillInfo = null;
        SelectedSkill = skillType;
    }

    public void UseSkill()
    {
        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        switch (SelectedSkill)
        {
            case SkillType.time_rewind:
                UseRewind();
                break;

            case SkillType.slow_motion:
                UseTimeSlow();
                break;

            case SkillType.missile_suppression:
                break;

            case SkillType.missile_slow:
                break;

            case SkillType.phase_dash:
                break;

            case SkillType.shockwave:
                break;


        }
    }

    private void UseRewind()
    {
        if (playerRewind == null)
            return;

        playerRewind.Rewind();
    }

    private void UseTimeSlow()
    {
        if (timeSlow == null)
            return;

        if (selectedSkillInfo != null)
            timeSlow.Activate(selectedSkillInfo.skillValue);
        else
            timeSlow.Activate();
    }

    private void UseShield()
    {
        if (shield == null) return;

        shield.Activate();
    }

    public bool IsSkillSelected()
    {
        return SelectedSkill != SkillType.None;
    }

    public bool IsSelected(SkillType skillType)
    {
        return SelectedSkill == skillType;
    }

    public void ResetSkill()
    {
        selectedSkillInfo = null;
        SelectedSkill = SkillType.None;

        if (playerRewind != null)
            playerRewind.ResetRewind();

        if (timeSlow != null)
            timeSlow.ResetSkill();

        if (shield != null)
            shield.ResetSkill();
    }
}
