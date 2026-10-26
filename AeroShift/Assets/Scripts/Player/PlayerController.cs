
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private FlightConfig config;
    [SerializeField] private Transform model;
    [SerializeField] private InputManager inputManager;

    private Vector2 mousePosition;
    private Vector3 targetPosition;
    private Vector3 smoothVelocity;
    private Quaternion targetRotation;
    private float currentSpeed;
    private float currentRoll;
    private float startZ;

    private void Start()
    {
        targetPosition = transform.position;
        startZ = transform.position.z;
        targetRotation = transform.rotation;

        if (config != null)
            currentSpeed = config.baseSpeed;
    }

    private void OnEnable()
    {
        if (inputManager == null)
            return;

        inputManager.Flight3DMoveChanged += OnFlight3DMoveChanged;
    }

    private void OnDisable()
    {
        if (inputManager == null)
            return;

        inputManager.Flight3DMoveChanged -= OnFlight3DMoveChanged;
    }

    private void OnFlight3DMoveChanged(Vector2 value)
    {
        mousePosition = value;
    }

    private void Update()
    {
        if (config == null || inputManager == null)
            return;

        ReadInput();
        UpdateFlightRotation();
        UpdateMovement();
    }

    private void ReadInput()
    {
        float normalizedX = Mathf.Clamp(
            mousePosition.x / Screen.width * 2f - 1f,
            -1f,
            1f
        );

        float normalizedY = Mathf.Clamp(
            mousePosition.y / Screen.height * 2f - 1f,
            -1f,
            1f
        );

        float horizontal = normalizedX * config.sensitivity;
        float vertical = normalizedY * config.sensitivity;

        float pitch = -vertical * config.maxPitch;
        float yaw = horizontal * config.maxYaw;

        targetRotation = Quaternion.Euler(pitch, yaw, 0f);

        currentRoll = Mathf.Lerp(
            currentRoll,
            -horizontal * config.maxRoll,
            Time.deltaTime * config.rollSmooth
        );

        float inputAmount = Mathf.Clamp01(
            Mathf.Abs(normalizedX) + Mathf.Abs(normalizedY)
        );

        float targetSpeed = Mathf.Lerp(
            config.baseSpeed,
            config.maxSpeed,
            inputAmount
        );

        targetSpeed = Mathf.Max(targetSpeed, config.minSpeed);

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            config.acceleration * Time.deltaTime
        );
    }

    private void UpdateFlightRotation()
    {
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * config.rotationSmooth
        );

        if (model == null)
            return;

        Quaternion modelRotation = Quaternion.Euler(
            0f,
            0f,
            currentRoll
        );

        model.localRotation = Quaternion.Slerp(
            model.localRotation,
            modelRotation,
            Time.deltaTime * config.rollSmooth
        );
    }

    private void UpdateMovement()
    {
        Vector3 flightDirection = transform.forward;

        Vector3 movement = new Vector3(
            flightDirection.x,
            flightDirection.y,
            0f
        );

        if (movement.sqrMagnitude > 0.001f)
            movement.Normalize();

        targetPosition += movement * currentSpeed * Time.deltaTime;

        targetPosition.x = Mathf.Clamp(
            targetPosition.x,
            -config.horizontalLimit,
            config.horizontalLimit
        );

        targetPosition.y = Mathf.Clamp(
            targetPosition.y,
            -config.verticalLimit,
            config.verticalLimit
        );

        targetPosition.z = startZ;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref smoothVelocity,
            config.smoothTime
        );

        Vector3 position = transform.position;
        position.z = startZ;
        transform.position = position;
    }
}
