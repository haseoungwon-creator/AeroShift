using UnityEngine;
using UnityEngine.UI;

public class SkillSelectController : MonoBehaviour
{
    [SerializeField] SkillManager skillManager;
    [SerializeField] Button rewindButton;
    [SerializeField] Button timeSlowButton;

    public void SelectRewind()
    {
        if (skillManager == null)
            return;

        skillManager.SelectSkill(SkillType.Rewind);
        UpdateSelection();
    }

    public void SelectTimeSlow()
    {
        if (skillManager == null)
            return;

        skillManager.SelectSkill(SkillType.TimeSlow);
        UpdateSelection();
    }

    public void UpdateSelection()
    {
        if (skillManager == null)
            return;

        SetButtonAlpha(
            rewindButton,
            skillManager.IsSelected(SkillType.Rewind));

        SetButtonAlpha(
            timeSlowButton,
            skillManager.IsSelected(SkillType.TimeSlow));
    }

    private void SetButtonAlpha(
        Button button,
        bool selected)
    {
        if (button == null)
            return;

        Color color = button.image.color;
        color.a = selected ? 1f : 0.4f;
        button.image.color = color;
    }
}