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

        skillManager.SelectSkill(SkillType.time_rewind);
        UpdateSelection();
    }

    public void SelectTimeSlow()
    {
        if (skillManager == null)
            return;

        skillManager.SelectSkill(SkillType.slow_motion);
        UpdateSelection();
    }

    public void UpdateSelection()
    {
        if (skillManager == null)
            return;

        SetButtonAlpha(
            rewindButton,
            skillManager.IsSelected(SkillType.time_rewind));

        SetButtonAlpha(
            timeSlowButton,
            skillManager.IsSelected(SkillType.slow_motion));
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