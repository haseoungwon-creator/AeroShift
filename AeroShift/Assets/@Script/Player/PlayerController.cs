using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerMode playerMode;
    [SerializeField] DimensionTransition dimensionTransition;
    [SerializeField] PlayerInput playerInput;
    [SerializeField] PlayerMovement3D playerMovement3D;
    [SerializeField] PlayerMovement2D playerMovement2D;
    [SerializeField] Rigidbody rb;
    [SerializeField] SkillManager skillManager;

    private bool canControl = true;

    public bool CanControl => canControl;

    private void Awake()
    {
        playerInput.actions.FindActionMap("System").Enable();
        playerInput.actions.FindActionMap("Player3D").Enable();
        playerInput.actions.FindActionMap("Player2D").Disable();
    }

    public void OnShift(InputAction.CallbackContext context)
    {
        if (!canControl || !context.performed)
            return;

        if (playerMode.Is3D())
            dimensionTransition.TransitionTo2D();
        else if (playerMode.IsMode2D())
            dimensionTransition.TransitionTo3D();
    }

    public void OnMousePosition(InputAction.CallbackContext context)
    {
        if (!canControl || playerMode == null || !playerMode.Is3D())
            return;

        playerMovement3D.OnMousePosition(context);
    }

    public void OnMouseClick(InputAction.CallbackContext context)
    {
        if (!canControl || playerMode == null || !playerMode.Is3D())
            return;

        playerMovement3D.OnMouseClick(context);
    }

    public void OnRewind(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (!canControl)
            return;

        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        if (skillManager == null)
            return;

        skillManager.UseSkill();
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!canControl || playerMode == null || !playerMode.IsMode2D())
            return;

        playerMovement2D.OnMove(context);
    }

    public void ResetPlayer()
    {
        playerInput.actions.FindActionMap("Player3D").Enable();
        playerInput.actions.FindActionMap("Player2D").Disable();

        playerMode.SetMode(PlayerModes.Mode3D);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.SetPositionAndRotation(
            new Vector3(0f, 10f, 0f),
            Quaternion.identity);

        playerMovement2D.SetControlEnabled(false);
        playerMovement3D.SetControlEnabled(false);

        canControl = false;
    }

    public void StopPlayer()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        playerMovement3D.SetControlEnabled(false);
        playerMovement2D.SetControlEnabled(false);

        canControl = false;
    }

    public void SetControlEnabled(bool value)
    {
        canControl = value;

        if (playerMode.Is3D())
            playerMovement3D.SetControlEnabled(value);
        else if (playerMode.IsMode2D())
            playerMovement2D.SetControlEnabled(value);
    }

    public void ResetMovementTarget()
    {
        playerMovement3D.ResetTargetPosition();
    }

    public void Set3DInput()
    {
        playerInput.actions.FindActionMap("Player3D").Enable();
        playerInput.actions.FindActionMap("Player2D").Disable();

        playerMovement2D.SetControlEnabled(false);
        playerMovement3D.SetControlEnabled(true);
    }

    public void Set2DInput()
    {
        playerInput.actions.FindActionMap("Player3D").Disable();
        playerInput.actions.FindActionMap("Player2D").Enable();

        playerMovement3D.SetControlEnabled(false);
        playerMovement2D.SetControlEnabled(true);
    }
}