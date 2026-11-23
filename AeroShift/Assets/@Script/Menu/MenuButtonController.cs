
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuButtonController : MonoBehaviour
{
    [SerializeField] private Animator menuAnimator;
    [SerializeField] private GameObject mainMenuUI;
    [SerializeField] private GameObject skinUI;
    [SerializeField] private GameObject modeSelectionUI;
    [SerializeField] private string gameSceneName = "Main";

    private bool isStarting;
    private bool isAnimating;
    private bool isLoading;

    private void Start()
    {
        ShowMainMenuUI();
        HindSkinUI();
        HideModeSelectionUI();
    }

    public void Play()
    {
        if (isStarting || isAnimating || menuAnimator == null)
            return;

        isStarting = true;
        isAnimating = true;
        menuAnimator.SetTrigger("Play");
    }

    public void ShowModeSelectionUI()
    {
        if (isLoading)
            return;

        if (mainMenuUI != null)
            mainMenuUI.SetActive(false);

        if (skinUI != null)
            skinUI.SetActive(false);

        if (modeSelectionUI != null)
            modeSelectionUI.SetActive(true);

        isStarting = false;
        isAnimating = false;
    }

    public void HideModeSelectionUI()
    {
        if (modeSelectionUI != null)
            modeSelectionUI.SetActive(false);
    }

    public void BackFromModeSelection()
    {
        if (isLoading)
            return;

        HideModeSelectionUI();
        ShowMainMenuUI();
    }

    public void PlayRegularMode()
    {
        LoadGame(GameMode.Regular);
    }

    public void PlayRandomMode()
    {
        LoadGame(GameMode.Random);
    }

    private void LoadGame(GameMode mode) 
    {
        if (isLoading)
            return;

        isLoading = true;
        isStarting = true;

        GameManager.SetSelectedGameMode(mode);

        StartCoroutine(LoadSceneAsync());
    }

    private IEnumerator LoadSceneAsync()
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneName);

        if (operation == null)
        {
            isLoading = false;
            isStarting = false;
            yield break;
        }

        yield return operation;
    }

    public void Airplane()
    {
        if (isStarting || isAnimating || menuAnimator == null)
            return;

        isAnimating = true;
        menuAnimator.SetTrigger("Skin");
    }

    public void Back()
    {
        if (isStarting || isAnimating || menuAnimator == null)
            return;

        isAnimating = true;
        menuAnimator.SetTrigger("Exit");
    }

    public void Quit()
    {
        Application.Quit();
    }

    public void HindMainUI()
    {
        if (mainMenuUI != null)
            mainMenuUI.SetActive(false);
    }

    public void ShowSkinUI()
    {
        if (skinUI != null)
            skinUI.SetActive(true);

        isAnimating = false;
    }

    public void HindSkinUI()
    {
        if (skinUI != null)
            skinUI.SetActive(false);
    }

    public void ShowMainMenuUI()
    {
        if (mainMenuUI != null)
            mainMenuUI.SetActive(true);

        isStarting = false;
        isAnimating = false;
    }
}
