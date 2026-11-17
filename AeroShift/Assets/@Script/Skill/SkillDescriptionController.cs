using UnityEngine;
using UnityEngine.UI;

public class SkillDescriptionController : MonoBehaviour
{
    [SerializeField] GameObject descriptionPanel;
    [SerializeField] Text skillName;
    [SerializeField] Text skillDescription;

    public void ShowDescription(string name, string description)
    {
        if (descriptionPanel != null)
            descriptionPanel.SetActive(true);

        if (skillName != null)
            skillName.text = name;

        if (skillDescription != null)
            skillDescription.text = description;
    }

    public void HideDescription()
    {
        if (descriptionPanel != null)
            descriptionPanel.SetActive(false);
    }
}