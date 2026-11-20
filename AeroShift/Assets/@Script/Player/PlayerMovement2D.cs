using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement2D : MonoBehaviour
{
    [SerializeField] float moveSpeed = 50f;
    [SerializeField] float moveSmooth = 15f;
    [SerializeField] float minX = -74f;
    [SerializeField] float maxX = 74f;
    [SerializeField] float minZ = -35f;
    [SerializeField] float maxZ = 35f;

    private Transform _t;
    private Vector2 currentInput;
    private Vector2 targetInput;
    private InputAction moveAction;
    private Vector3 targetPosition;
    private bool canControl;

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

        if (moveAction != null)
            targetInput = moveAction.enabled ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        UpdateInput();
        Move();
    }

    public void BindMoveAction(InputAction action)
    {
        moveAction = action;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!canControl)
            return;

        targetInput = context.ReadValue<Vector2>();
    }

    private void UpdateInput()
    {
        currentInput = Vector2.Lerp(
            currentInput,
            targetInput,
            moveSmooth * Time.deltaTime);
    }

    private void Move()
    {
        Vector3 movement = new Vector3(
            currentInput.x,
            0f,
            currentInput.y);

        targetPosition += movement * moveSpeed * Time.deltaTime;

        targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
        targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

        targetPosition.y = _t.position.y;
        _t.position = Vector3.Lerp(
            _t.position,
            targetPosition,
            moveSmooth * Time.deltaTime);
    }

    public void ApplyTransitionOffset(Vector3 offset)
    {
        if (!canControl)
            return;

        Vector3 position = _t.position + offset;
        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);

        Vector3 applied = position - _t.position;
        _t.position = position;
        targetPosition += applied;
        targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
        targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        targetPosition.y = position.y;
    }

    public void SetControlEnabled(bool value)
    {
        canControl = value;
        ResetMovement();
    }

    private void ResetMovement()
    {
        currentInput = Vector2.zero;
        targetInput = Vector2.zero;
        targetPosition = _t.position;
    }
}