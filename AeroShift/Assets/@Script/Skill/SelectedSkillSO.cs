using UnityEngine;

[CreateAssetMenu(fileName = "SelectedSkill", menuName = "AeroShift/Selected Skill")]
public class SelectedSkillSO : ScriptableObject
{
    [SerializeField] private SkillInfo selectedSkill;

    public SkillInfo CurrentSkill
    {
        get { return selectedSkill; }
    }

    public void ClearSkill()
    {
        selectedSkill = null;
    }

    public void SetSkill(SkillInfo info)
    {
        if (info == null)
            return;

        selectedSkill = info;
    }
}
