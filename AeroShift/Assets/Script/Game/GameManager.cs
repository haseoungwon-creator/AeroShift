using System.Collections;
using UnityEngine;

public enum GameStates
{
    Ready,
    Playing,
    GameOver
}

public class GameManager : MonoBehaviour
{
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
        cloudSpawner.Set3DMode();
        mapManager.ResetMaps();
        worldMoveMent.ResetPosition();
        cameraController.ResetCamera();
        scoreManager.ResetScore();
        SetGameState(GameStates.Ready);
        playerController.ResetPlayer();
        playerRewind.ResetRewind();

        nearMissDetector.ResetDetector();

    }
}