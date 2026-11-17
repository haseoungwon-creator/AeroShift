using UnityEngine;
using UnityEngine.UI;

public class SkillSelectionUI : MonoBehaviour
{
    [SerializeField] private Text titleText;
    [SerializeField] private Text descriptionText;

    private SkillSelectionManager selectionManager;
    private SkillInfo skillInfo;

    public void SetSkill(SkillInfo info, SkillSelectionManager manager)
    {
        skillInfo = info;
        selectionManager = manager;

        if (skillInfo == null)
            return;

        if (titleText != null)
            titleText.text = skillInfo.name;

        if (descriptionText != null)
            descriptionText.text = skillInfo.description;
    }

    public void Select()
    {
        if (selectionManager == null || skillInfo == null)
            return;

        selectionManager.SelectSkill(skillInfo);
    }
}
