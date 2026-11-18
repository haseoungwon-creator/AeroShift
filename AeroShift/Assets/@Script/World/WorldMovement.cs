using UnityEngine;

public class WorldMovement : MonoBehaviour
{
    [SerializeField] float worldSpeed = 30f;
    [SerializeField] float maxWorldSpeed = 60f;
    [SerializeField] float speedIncrease = 5f;
    [SerializeField] float speedIncreaseInterval = 10f;
    [SerializeField] bool canMove;

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

    public void SetSpeed(float speed)
    {
        currentSpeed = speed;
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
}
