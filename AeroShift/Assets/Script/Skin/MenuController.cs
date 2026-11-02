using System.Collections;
using UnityEngine;

public class MenuController : MonoBehaviour
{
    [SerializeField] CameraController cameraController;
    [SerializeField] PlayerController playerController;
    [SerializeField] SkinPreviewController skinPreviewController;
    [SerializeField] GameUI gameUI;

    public void OpenSkinSelect()
    {
        if (cameraController == null)
            return;

        if (gameUI != null)
            gameUI.HideAll();

        StartCoroutine(OpenSkinSelectSequence());
    }

    private IEnumerator OpenSkinSelectSequence()
    {
        if (playerController != null)
            playerController.SetControlEnabled(false);

        yield return cameraController.MoveToGamePosition();

        if (gameUI != null)
            gameUI.ShowSkinSelect();

        if (skinPreviewController != null)
        {
            skinPreviewController.UpdatePreview();
            skinPreviewController.SetPreviewMode(true);
        }
    }

    public void CloseSkinSelect()
    {
        StartCoroutine(CloseSkinSelectSequence());
    }

    private IEnumerator CloseSkinSelectSequence()
    {
        if (skinPreviewController != null)
            skinPreviewController.ResetPreview();

        if (gameUI != null)
            gameUI.HideSkinSelect();

        if (playerController != null)
            playerController.SetControlEnabled(false);

        yield return cameraController.MoveFromGamePosition();

        if (gameUI != null)
            gameUI.ShowStartPanel();
    }

    public void OpenSkillSelect()
    {
        if (gameUI == null)
            return;

        gameUI.ShowSkillSelect();
    }

    public void CloseSkillSelect()
    {
        if (gameUI == null)
            return;

        gameUI.ShowStartPanel();
    }
}