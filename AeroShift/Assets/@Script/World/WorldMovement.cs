using UnityEngine;

public class WorldMovement : MonoBehaviour
{
    [SerializeField] private TextAsset settingsJson;
    private bool settingsLoaded;

    private float worldSpeed;
    private float maxWorldSpeed;
    private float speedIncrease;
    private float speedIncreaseInterval;
    [SerializeField] private bool canMove;

    public float CurrentSpeed
    {
        get { return currentSpeed * statSpeedMultiplier * temporarySpeedMultiplier + dashSpeedBonus; }
    }

    public bool IsRewinding { get; private set; }

    private float currentSpeed;
    private float speedTimer;
    private float statSpeedMultiplier = 1f;
    private float temporarySpeedMultiplier = 1f;
    private float dashSpeedBonus;

    private void Awake()
    {
        LoadSettings();
        currentSpeed = worldSpeed;
    }

    private void Update()
    {
        if (!canMove || IsRewinding)
            return;

        transform.position += Vector3.back * CurrentSpeed * Time.deltaTime;

        speedTimer += Time.deltaTime;

        if (speedTimer >= speedIncreaseInterval)
        {
            speedTimer = 0f;
            currentSpeed = Mathf.Min(currentSpeed + speedIncrease, maxWorldSpeed);
        }
    }

    public void SetStatSpeedMultiplier(float value)
    {
        statSpeedMultiplier = value;
    }

    public void SetTemporarySpeedMultiplier(float value)
    {
        temporarySpeedMultiplier = value;
    }

    public void SetDashSpeedBonus(float value)
    {
        dashSpeedBonus = Mathf.Max(0f, value);
    }

    public void SetMovement(bool value)
    {
        canMove = value;
    }

    public void SetRewinding(bool value)
    {
        IsRewinding = value;
    }

    public void ResetPosition()
    {
        transform.position = Vector3.zero;
        currentSpeed = worldSpeed;
        speedTimer = 0f;
        temporarySpeedMultiplier = 1f;
        dashSpeedBonus = 0f;
        IsRewinding = false;
    }

    private void LoadSettings()
    {
        if (settingsLoaded)
            return;

        WorldMovementSettings data = JsonSettingsStore.Load<WorldMovementSettings>(
            settingsJson, "Config/Settings/WorldMovement");
        worldSpeed = data.worldSpeed;
        maxWorldSpeed = data.maxWorldSpeed;
        speedIncrease = data.speedIncrease;
        speedIncreaseInterval = data.speedIncreaseInterval;
        settingsLoaded = true;
    }
}
