using System;
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] PlayerMode playerMode;
    [SerializeField] int nearMissScore = 5;
    [SerializeField] float distanceScoreMultiplier = 0.05f;

    public int CurrentScore { get; private set; }
    public int HighScore { get; private set; }

    public event Action<int> OnScoreChanged;
    public event Action<int> OnHighScoreChanged;

    private const string HighScoreKey = "HighScore";

    private float accumulatedDistance;
    private bool scoringEnabled;
    private bool isRewinding;

    private void Awake()
    {
        LoadHighScore();
    }

    private void Update()
    {
        if (isRewinding)
            return;

        if (!scoringEnabled || worldMovement == null)
            return;

        AddDistanceScore(
            worldMovement.CurrentSpeed * Time.deltaTime);
    }

    private void AddDistanceScore(float distance)
    {
        accumulatedDistance +=
            distance * distanceScoreMultiplier;

        int score =
            Mathf.FloorToInt(accumulatedDistance);

        if (score <= 0)
            return;

        accumulatedDistance -= score;
        CurrentScore += score;

        OnScoreChanged?.Invoke(CurrentScore);

        UpdateHighScore();
    }

    public void AddNearMiss()
    {
        if (isRewinding)
            return;

        if (!scoringEnabled ||
            playerMode == null ||
            !playerMode.Is3D())
            return;

        CurrentScore += nearMissScore;

        OnScoreChanged?.Invoke(CurrentScore);

        UpdateHighScore();
    }

    public void SetRewinding(bool value)
    {
        isRewinding = value;
    }

    public void StartScoring()
    {
        scoringEnabled = true;
    }

    public void StopScoring()
    {
        scoringEnabled = false;
    }

    public void ResetScore()
    {
        CurrentScore = 0;
        accumulatedDistance = 0f;

        OnScoreChanged?.Invoke(CurrentScore);
    }

    public void SaveHighScore()
    {
        PlayerPrefs.SetInt(
            HighScoreKey,
            HighScore);

        PlayerPrefs.Save();
    }

    private void LoadHighScore()
    {
        HighScore =
            PlayerPrefs.GetInt(
                HighScoreKey,
                0);
    }

    private void UpdateHighScore()
    {
        if (CurrentScore <= HighScore)
            return;

        HighScore = CurrentScore;

        OnHighScoreChanged?.Invoke(HighScore);
    }
}