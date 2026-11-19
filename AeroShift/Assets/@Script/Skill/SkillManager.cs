using UnityEngine;

public enum SkillType
{
    None,
    time_rewind,
    slow_motion,
    phase_shield,
    missile_suppression,//
    missile_slow,/**/
    phase_dash,
    shockwave//
}

public class SkillManager : MonoBehaviour
{
    [SerializeField] PlayerRewind playerRewind;
    [SerializeField] TimeSlow timeSlow;
    [SerializeField] Shield shield;
    [SerializeField] PhaseDash phaseDash;
    [SerializeField] Missile_slow missileSlow;

    private SkillInfo selectedSkillInfo;

    public SkillType SelectedSkill { get; private set; } = SkillType.None;

    public void SetSelectedSkill(SkillInfo info)
    {
        if (info != null && selectedSkillInfo != null && info.id == selectedSkillInfo.id)
            return;

        if (shield != null)
            shield.ResetSkill();

        if (phaseDash != null)
            phaseDash.ResetSkill();

        if(missileSlow != null)
            missileSlow.ResetSkill();

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
        else if (info.id == "phase_dash")
        {
            SelectedSkill = SkillType.phase_dash;
        }
        else if (info.id == "phase_shield")
        {
            SelectedSkill = SkillType.phase_shield;

            if (shield != null)
                shield.Activate();
        }
        else if (info.id == "missile_slow")
        {
            SelectedSkill = SkillType.missile_slow;
        }
    }

    public void SelectSkill(SkillType skillType)
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameStates.Ready)
            return;

        if (shield != null)
            shield.ResetSkill();

        if (phaseDash != null)
            phaseDash.ResetSkill();

        if(missileSlow != null)
            missileSlow.ResetSkill();

        selectedSkillInfo = null;
        SelectedSkill = skillType;

        if (skillType == SkillType.phase_shield && shield != null)
            shield.Activate();
    }

    public void UseSkill()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        switch (SelectedSkill)
        {
            case SkillType.time_rewind:
                UseRewind();
                break;

            case SkillType.slow_motion:
                UseTimeSlow();
                break;

            case SkillType.phase_dash:
                UseDash();
                break;

            case SkillType.missile_slow:
                UseMissileSlow();
                break;
                
        }
    }

    private void UseRewind()
    {
        if (playerRewind != null)
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

    private void UseDash()
    {
        if (phaseDash == null)
            return;

        float distance = 300f;

        if (selectedSkillInfo != null)
            distance = selectedSkillInfo.skillValue;

        phaseDash.Activate(distance);
    }

    private void UseMissileSlow()
    {
        if (missileSlow == null)
            return;

        float multiplier = selectedSkillInfo != null ? selectedSkillInfo.skillValue : 0.6f;

        missileSlow.Activate(multiplier);
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

        if (phaseDash != null)
            phaseDash.ResetSkill();
    }
}
