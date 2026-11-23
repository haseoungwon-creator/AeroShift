using UnityEngine;

public class MenuAnimationEvents : MonoBehaviour
{
    [SerializeField] private MenuButtonController menuButtonController;

    public void HindMainUI()
    {
        if (menuButtonController != null)
            menuButtonController.HindMainUI();
    }

    public void ShowSkinUI()
    {
        if (menuButtonController != null)
            menuButtonController.ShowSkinUI();
    }

    public void HindSkinUI()
    {
        if (menuButtonController != null)
            menuButtonController.HindSkinUI();
    }

    public void ShowMainMenuUI()
    {
        if (menuButtonController != null)
            menuButtonController.ShowMainMenuUI();
    }

}
