using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement3D : MonoBehaviour
{
    [SerializeField] float moveSpeed = 25f;
    [SerializeField] float verticalMultiplier = 2f;
    [SerializeField] float moveSmooth = 15f;
    [SerializeField] float clickMoveMultiplier = 0.2f;

    [SerializeField] float minX = -40f;
    [SerializeField] float maxX = 40f;
    [SerializeField] float minY = 3f;
    [SerializeField] float maxY = 50f;

    [SerializeField] float maxPitch = 10f;
    [SerializeField] float maxRoll = 18f;
    [SerializeField] float clickMaxRoll = 130f;
    [SerializeField] float rotationSmooth = 5f;

    [SerializeField] float inputSensitivity = 1.5f;
    [SerializeField] float inputSmooth = 8f;

    private Transform _t;
    private Vector2 currentInput;
    private Vector2 targetInput;
    private Vector3 targetPosition;
    private float rawMouseX;
    private bool clickHeld;
    private bool canControl = true;

    private void Awake()
    {
        _t = transform;
    }

    private void Start()
    {
        targetPosition = _t.position;
    }

    private void Update()
    {
        if (!canControl)
            return;

        UpdateInput();
        Move();
        Rotate();
    }

    public void OnMousePosition(InputAction.CallbackContext context)
    {
        if (!canControl || !context.performed)
            return;

        Vector2 mousePosition = context.ReadValue<Vector2>();
        Vector2 center = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f);

        targetInput = new Vector2(
            (mousePosition.x - center.x) / center.x,
            (mousePosition.y - center.y) / center.y);

        targetInput *= inputSensitivity;

        targetInput.x = Mathf.Clamp(
            targetInput.x,
            -1f,
            1f);

        targetInput.y = Mathf.Clamp(
            targetInput.y,
            -1f,
            1f);
    }

    public void OnMouseClick(InputAction.CallbackContext context)
    {
        if (!canControl)
            return;

        clickHeld = context.ReadValueAsButton();
    }

    private void UpdateInput()
    {
        currentInput = Vector2.Lerp(
            currentInput,
            targetInput,
            inputSmooth * Time.deltaTime);

        rawMouseX = Mathf.Clamp(
            currentInput.x,
            -1f,
            1f);
    }

    private void Move()
    {
        Vector3 right = _t.right;
        Vector3 up = _t.up;

        right.z = 0f;
        up.z = 0f;

        if (right.sqrMagnitude > 0.001f)
            right.Normalize();

        if (up.sqrMagnitude > 0.001f)
            up.Normalize();

        float moveMultiplier =
            clickHeld ? clickMoveMultiplier : 1f;

        Vector3 movement =
            right * currentInput.x * verticalMultiplier +
            up * currentInput.y * verticalMultiplier;

        targetPosition +=
            movement *
            moveSpeed *
            moveMultiplier *
            Time.deltaTime;

        targetPosition.x = Mathf.Clamp(
            targetPosition.x,
            minX,
            maxX);

        targetPosition.y = Mathf.Clamp(
            targetPosition.y,
            minY,
            maxY);

        _t.position = Vector3.Lerp(
            _t.position,
            targetPosition,
            moveSmooth * Time.deltaTime);
    }

    private void Rotate()
    {
        float targetPitch =
            -currentInput.y * maxPitch;

        float rollLimit =
            clickHeld ? clickMaxRoll : maxRoll;

        Quaternion targetRotation =
            Quaternion.Euler(
                targetPitch,
                0f,
                -rawMouseX * rollLimit);

        _t.rotation = Quaternion.Slerp(
            _t.rotation,
            targetRotation,
            rotationSmooth * Time.deltaTime);
    }

    public void SetControlEnabled(bool value)
    {
        canControl = value;

        if (!value)
            ResetMovement();
    }

    public void ResetTargetPosition()
    {
        targetPosition = _t.position;
        currentInput = Vector2.zero;
        targetInput = Vector2.zero;
        rawMouseX = 0f;
        clickHeld = false;
    }

    private void ResetMovement()
    {
        currentInput = Vector2.zero;
        targetInput = Vector2.zero;
        rawMouseX = 0f;
        clickHeld = false;
        targetPosition = _t.position;
    }
}