
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private FlightConfig config;
    [SerializeField] private Transform model;

    private Vector3 targetPosition;
    private Vector3 smoothVelocity;
    private Vector3 previousPosition;
    private float currentRoll;
    private float currentPitch;

    private void Start()
    {
        targetPosition = transform.position;
        previousPosition = transform.position;
    }

    private void Update()
    {
        if (config == null || Mouse.current == null)
            return;

        UpdateMovement();
        UpdateModelTilt();
    }

    private void UpdateMovement()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        float normalizedX = Mathf.Clamp01(mousePosition.x / Screen.width);
        float normalizedY = Mathf.Clamp01(mousePosition.y / Screen.height);

        float horizontal = (normalizedX * 2f - 1f) * config.horizontalLimit * config.sensitivity;
        float vertical = (normalizedY * 2f - 1f) * config.verticalLimit * config.sensitivity;

        targetPosition = new Vector3(
            Mathf.Clamp(horizontal, -config.horizontalLimit, config.horizontalLimit),
            Mathf.Clamp(vertical, -config.verticalLimit, config.verticalLimit),
            transform.position.z
        );

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref smoothVelocity,
            config.smoothTime
        );
    }

    private void UpdateModelTilt()
    {
        Vector3 movement = transform.position - previousPosition;

        float targetRoll = -movement.x * config.maxRoll * 4f;
        float targetPitch = movement.y * config.maxPitch * 4f;

        targetRoll = Mathf.Clamp(targetRoll, -config.maxRoll, config.maxRoll);
        targetPitch = Mathf.Clamp(targetPitch, -config.maxPitch, config.maxPitch);

        currentRoll = Mathf.Lerp(
            currentRoll,
            targetRoll,
            Time.deltaTime * config.tiltSmooth
        );

        currentPitch = Mathf.Lerp(
            currentPitch,
            targetPitch,
            Time.deltaTime * config.tiltSmooth
        );

        if (model != null)
        {
            model.localRotation = Quaternion.Euler(
                currentPitch,
                0f,
                currentRoll
            );
        }

        previousPosition = transform.position;
    }
}
