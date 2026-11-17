using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuButtonController : MonoBehaviour
{
    [SerializeField] private Animator menuAnimator;
    [SerializeField] private GameObject mainMenuUI;
    [SerializeField] private GameObject skinUI;
    [SerializeField] private string gameSceneName = "Main";

    private bool isStarting;
    private bool isAnimating;
    private bool isLoading;

    private void Start()
    {
        ShowMainMenuUI();
        HindSkinUI();
    }

    public void Play()
    {
        if (isStarting || isAnimating || menuAnimator == null)
            return;

        isStarting = true;
        isAnimating = true;
        menuAnimator.SetTrigger("Play");
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

        isAnimating = false;
    }

    // Play 애니메이션의 마지막 Animation Event에서 호출
    public void LoadGameScene()
    {
        if (isLoading)
            return;

        StartCoroutine(LoadSceneAsync());
    }

    private IEnumerator LoadSceneAsync()
    {
        isLoading = true;

        AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneName);

        if (operation == null)
        {
            isLoading = false;
            yield break;
        }

        yield return operation;
    }
}
