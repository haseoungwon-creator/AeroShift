using System.Collections;
using UnityEngine;
using Unity.Profiling;

public enum GameStates
{
    Ready,
    Playing,
    GameOver
}

public class GameManager : MonoBehaviour
{
    private static readonly ProfilerMarker MapResetMarker = new ProfilerMarker("AeroShift.Game.MapReset");
    public static GameManager Instance { get; private set; }

    [SerializeField] WorldMovement worldMoveMent;
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] MissileSpawner missileSpawner;
    [SerializeField] GameUI gameUI;
    [SerializeField] PlayerController playerController;
    [SerializeField] CameraController cameraController;
    [SerializeField] MapManager mapManager;
    [SerializeField] NearMissDetector nearMissDetector;
    [SerializeField] PlayerRewind playerRewind;
    [SerializeField] GameOverUI gameOverUI;
    [SerializeField] CloudSpawner cloudSpawner;

    public GameStates CurrentState { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CurrentState = GameStates.Ready;
    }

    private void Start()
    {
        SetGameState(GameStates.Ready);
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
        Debug.Log("Gameover");
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
                playerController.SetControlEnabled(false);
                gameUI.ShowStartPanel();
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
        }
    }
    public void RestartGame()
    {
        if (playerRewind != null)
            playerRewind.ResetRewind();

        if (nearMissDetector != null)
            nearMissDetector.ResetDetector();

        if (cloudSpawner != null)
            cloudSpawner.Set3DMode();

        SetGameState(GameStates.Ready);
    }
}
