using UnityEngine;
using UnityEngine.EventSystems;

public class SkillHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] SkillDescriptionController descriptionController;
    [SerializeField] string skillName;
    [SerializeField] string skillDescription;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (descriptionController == null)
            return;

        descriptionController.ShowDescription(
            skillName,
            skillDescription);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (descriptionController == null)
            return;

        descriptionController.HideDescription();
    }
}