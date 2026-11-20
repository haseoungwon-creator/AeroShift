using UnityEngine;

public class GameUI : MonoBehaviour
{
    [SerializeField] GameObject startPanel;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] GameObject skinSelectPanel;
    [SerializeField] GameObject skillSelectPanel;

    public void ShowGameOverPanel()
    {
        HideAll();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);
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