
using System.Collections;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.SceneManagement;

public enum GameStates
{
    Ready,
    Playing,
    GameOver,
    None
}

public enum GameMode
{
    Regular,
    Random
}

public class GameManager : MonoBehaviour
{
    private static readonly ProfilerMarker MapResetMarker =
        new ProfilerMarker("AeroShift.Game.MapReset");

    public static GameManager Instance { get; private set; }

    public static bool IsPlaying =>
        Instance != null &&
        Instance.CurrentState == GameStates.Playing;

    public static GameMode SelectedGameMode { get; private set; } =
        GameMode.Regular;

    [SerializeField] private string menuScene = "Menu";
    [SerializeField] private WorldMovement worldMoveMent;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private MissileSpawner missileSpawner;
    [SerializeField] private GameUI gameUI;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private PlayerRewind playerRewind;
    [SerializeField] private GameOverUI gameOverUI;
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private SkillSelectionManager skillSelectionManager;

    public GameStates CurrentState { get; private set; }

    public GameMode CurrentGameMode =>
        mapManager != null
            ? mapManager.CurrentGameMode
            : SelectedGameMode;

    public static void SetSelectedGameMode(GameMode mode)
    {
        SelectedGameMode = mode;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CurrentState = GameStates.Ready;

        if (mapManager != null)
            mapManager.SetGameMode(SelectedGameMode);
    }

    private void Start()
    {
        SetGameState(GameStates.Ready);
    }

    public void SelectRegularMode()
    {
        SetGameMode(GameMode.Regular);
    }

    public void SelectRandomMode()
    {
        SetGameMode(GameMode.Random);
    }
    
    public void SetGameMode(GameMode mode)
    {
        if (CurrentState == GameStates.Playing || mapManager == null)
            return;

        mapManager.SetGameMode(mode);
        SelectedGameMode = mapManager.CurrentGameMode;
    }

    public void StartGame()
    {
        StartCoroutine(StartGameSequence());
    }

    private IEnumerator StartGameSequence()
    {
        gameUI.HideAll();
        playerController.SetControlEnabled(false);

        yield return cameraController.MoveToGamePosition();

        SetGameState(GameStates.Playing);
    }

    public void GameOver()
    {
        SetGameState(GameStates.GameOver);
    }

    private void SetGameState(GameStates state)
    {
        CurrentState = state;

        switch (state)
        {
            case GameStates.Ready:
                worldMoveMent.SetMovement(false);
                worldMoveMent.ResetPosition();
                scoreManager.StopScoring();
                scoreManager.ResetScore();
                missileSpawner.SetSpawning(false);
                playerController.ResetPlayer();

                using (MapResetMarker.Auto())
                    mapManager.ResetMaps();

                cameraController.ResetCamera();
                skillSelectionManager.ShowRandomSkills();
                break;

            case GameStates.Playing:
                worldMoveMent.SetMovement(true);
                scoreManager.StartScoring();
                playerController.SetControlEnabled(true);
                break;

            case GameStates.GameOver:
                worldMoveMent.SetMovement(false);
                scoreManager.StopScoring();
                missileSpawner.SetSpawning(false);
                scoreManager.SaveHighScore();
                playerController.StopPlayer();
                gameUI.ShowGameOverPanel();
                gameOverUI.ShowScore();
                break;

            case GameStates.None:
                SceneManager.LoadScene(menuScene);
                break;
        }
    }

    public void RestartGame()
    {
        if (playerRewind != null)
            playerRewind.ResetRewind();

        if (cloudSpawner != null)
            cloudSpawner.Set3DMode();

        gameUI.HideAll();

        SetGameState(GameStates.Ready);
    }

    public void BackGame()
    {
        SetGameState(GameStates.None);
    }
}
