using UnityEngine;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] ScoreManager scoreManager;
    [SerializeField] Text scoreText;
    [SerializeField] Text highScoreText;

    public void ShowScore()
    {
        if (scoreManager == null)
            return;

        if (scoreText != null)
            scoreText.text = scoreManager.CurrentScore.ToString("D6");

        if (highScoreText != null)
            highScoreText.text = scoreManager.HighScore.ToString("D6");
    }
}