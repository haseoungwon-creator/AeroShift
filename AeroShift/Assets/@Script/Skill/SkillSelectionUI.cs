using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SkillSelectionUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Text titleText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Image skillImage;
    [SerializeField] private Button rerollButton;

    private SkillSelectionManager selectionManager;
    private SkillInfo skillInfo;

    public SkillInfo CurrentSkill
    {
        get { return skillInfo; }
    }

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

        if (skillImage != null)
        {
            Sprite sprite = Resources.Load<Sprite>("SkillIcons/" + skillInfo.id);
            skillImage.sprite = sprite;
            skillImage.enabled = sprite != null;
        }
    }

    public void Select()
    {
        if (selectionManager == null || skillInfo == null)
            return;

        selectionManager.SelectSkill(skillInfo);
    }

    public void Reroll()
    {
        if (selectionManager == null)
            return;

        selectionManager.RerollSkill(this);
    }

    public void SetRerollAvailable(bool available)
    {
        if (rerollButton != null)
            rerollButton.interactable = available;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Select();
    }
}
