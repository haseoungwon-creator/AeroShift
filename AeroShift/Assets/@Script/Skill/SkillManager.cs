using UnityEngine;

public enum SkillType
{
    None,
    Rewind,
    TimeSlow
}

public class SkillManager : MonoBehaviour
{
    [SerializeField] PlayerRewind playerRewind;
    [SerializeField] TimeSlow timeSlow;

    public SkillType SelectedSkill { get; private set; } = SkillType.None;

    public void SelectSkill(SkillType skillType)
    {
        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Ready)
            return;

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
            case SkillType.Rewind:
                UseRewind();
                break;

            case SkillType.TimeSlow:
                UseTimeSlow();
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

        timeSlow.Activate();
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
        SelectedSkill = SkillType.None;

        if (playerRewind != null)
            playerRewind.ResetRewind();

        if (timeSlow != null)
            timeSlow.ResetSkill();
    }
}