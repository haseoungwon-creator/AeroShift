using UnityEngine;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] Text scoreText;
    [SerializeField] Text highScoreText;

    private void OnEnable()
    {
        if (scoreManager == null)
            return;

        scoreManager.OnScoreChanged += UpdateScore;
        scoreManager.OnHighScoreChanged += UpdateHighScore;
    }

    private void Start()
    {
        if (scoreManager == null)
            return;

        UpdateScore(scoreManager.CurrentScore);
        UpdateHighScore(scoreManager.HighScore);
    }

    private void OnDisable()
    {
        if (scoreManager == null)
            return;

        scoreManager.OnScoreChanged -= UpdateScore;
        scoreManager.OnHighScoreChanged -= UpdateHighScore;
    }

    private void UpdateScore(int score)
    {
        if (scoreText == null)
            return;

        scoreText.text = score.ToString("D6");
    }

    private void UpdateHighScore(int highScore)
    {
        if (highScoreText == null)
            return;

        highScoreText.text = highScore.ToString("D6");
    }
}