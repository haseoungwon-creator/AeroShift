using UnityEngine;

public class WorldMovement : MonoBehaviour
{
    [SerializeField] float worldSpeed = 30f;
    [SerializeField] float maxWorldSpeed = 60f;
    [SerializeField] float speedIncrease = 5f;
    [SerializeField] float speedIncreaseInterval = 10f;
    [SerializeField] bool canMove;

    public float CurrentSpeed => currentSpeed;
    public bool IsRewinding { get; private set; }

    private float currentSpeed;
    private float speedTimer;

    private void Awake()
    {
        currentSpeed = worldSpeed;
    }

    private void Update()
    {
        if (!canMove || IsRewinding)
            return;

        transform.position += Vector3.back * currentSpeed * Time.deltaTime;

        speedTimer += Time.deltaTime;

        if (speedTimer >= speedIncreaseInterval)
        {
            speedTimer = 0f;

            currentSpeed = Mathf.Min(
                currentSpeed + speedIncrease,
                maxWorldSpeed);
        }
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
        IsRewinding = false;
    }
}