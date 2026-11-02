using UnityEngine;

public class GameUI : MonoBehaviour
{
    [SerializeField] GameObject startPanel;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] GameObject skinSelectPanel;
    [SerializeField] GameObject skillSelectPanel;

    public void ShowStartPanel()
    {
        HideAll();

        if (startPanel != null)
            startPanel.SetActive(true);
    }

    public void ShowGameOverPanel()
    {
        HideAll();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);
    }

    public void ShowSkinSelect()
    {
        HideAll();

        if (skinSelectPanel != null)
            skinSelectPanel.SetActive(true);
    }

    public void ShowSkillSelect()
    {
        HideAll();

        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(true);
    }

    public void HideSkinSelect()
    {
        if (skinSelectPanel != null)
            skinSelectPanel.SetActive(false);
    }

    public void HideSkillSelect()
    {
        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(false);
    }

    public void HideAll()
    {
        if (startPanel != null)
            startPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (skinSelectPanel != null)
            skinSelectPanel.SetActive(false);

        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(false);
    }
}