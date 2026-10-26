
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionAsset inputActions;

    public event Action<Vector2> Flight3DMoveChanged;
    public event Action<Vector2> Flight2DMoveChanged;
    public event Action PauseRequested;
    public event Action SkillRequested;
    public event Action DimensionSwitchRequested;

    private InputActionMap systemMap;
    private InputActionMap flight3DMap;
    private InputActionMap flight2DMap;

    private InputAction pauseAction;
    private InputAction skillAction;
    private InputAction dimensionSwitchAction;
    private InputAction flight3DMoveAction;
    private InputAction flight2DMoveAction;

    private bool isInitialized;

    private void Awake()
    {
        if (inputActions == null)
        {
            Debug.LogError("Input Actions 에셋이 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        systemMap = inputActions.FindActionMap("System", true);
        flight3DMap = inputActions.FindActionMap("Flight3D", true);
        flight2DMap = inputActions.FindActionMap("Flight2D", true);

        pauseAction = systemMap.FindAction("Pause", true);
        skillAction = systemMap.FindAction("UseSkill", true);
        dimensionSwitchAction = systemMap.FindAction("DimensionSwitch", true);

        flight3DMoveAction = flight3DMap.FindAction("Move", true);
        flight2DMoveAction = flight2DMap.FindAction("Move", true);

        pauseAction.performed += OnPause;
        skillAction.performed += OnUseSkill;
        dimensionSwitchAction.performed += OnDimensionSwitch;

        flight3DMoveAction.performed += OnFlight3DMove;
        flight3DMoveAction.canceled += OnFlight3DMoveCanceled;

        flight2DMoveAction.performed += OnFlight2DMove;
        flight2DMoveAction.canceled += OnFlight2DMoveCanceled;

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized)
            return;

        systemMap.Enable();
        SetDimension3D();
    }

    private void OnDisable()
    {
        if (!isInitialized)
            return;

        systemMap.Disable();
        flight3DMap.Disable();
        flight2DMap.Disable();
    }

    public void SetDimension3D()
    {
        if (!isInitialized)
            return;

        flight2DMap.Disable();
        flight3DMap.Enable();

        Flight3DMoveChanged?.Invoke(
            flight3DMoveAction.ReadValue<Vector2>()
        );
    }

    public void SetDimension2D()
    {
        if (!isInitialized)
            return;

        flight3DMap.Disable();
        flight2DMap.Enable();

        Flight2DMoveChanged?.Invoke(
            flight2DMoveAction.ReadValue<Vector2>()
        );
    }

    public void SetGameplayInputEnabled(bool isEnabled)
    {
        if (!isInitialized)
            return;

        if (isEnabled)
        {
            systemMap.Enable();
            SetDimension3D();
        }
        else
        {
            systemMap.Disable();
            flight3DMap.Disable();
            flight2DMap.Disable();
        }
    }

    private void OnPause(InputAction.CallbackContext context)
    {
        PauseRequested?.Invoke();
    }

    private void OnUseSkill(InputAction.CallbackContext context)
    {
        SkillRequested?.Invoke();
    }

    private void OnDimensionSwitch(InputAction.CallbackContext context)
    {
        DimensionSwitchRequested?.Invoke();
    }

    private void OnFlight3DMove(InputAction.CallbackContext context)
    {
        Flight3DMoveChanged?.Invoke(context.ReadValue<Vector2>());
    }

    private void OnFlight3DMoveCanceled(InputAction.CallbackContext context)
    {
        Flight3DMoveChanged?.Invoke(Vector2.zero);
    }

    private void OnFlight2DMove(InputAction.CallbackContext context)
    {
        Flight2DMoveChanged?.Invoke(context.ReadValue<Vector2>());
    }

    private void OnFlight2DMoveCanceled(InputAction.CallbackContext context)
    {
        Flight2DMoveChanged?.Invoke(Vector2.zero);
    }

    private void OnDestroy()
    {
        if (!isInitialized)
            return;

        pauseAction.performed -= OnPause;
        skillAction.performed -= OnUseSkill;
        dimensionSwitchAction.performed -= OnDimensionSwitch;

        flight3DMoveAction.performed -= OnFlight3DMove;
        flight3DMoveAction.canceled -= OnFlight3DMoveCanceled;

        flight2DMoveAction.performed -= OnFlight2DMove;
        flight2DMoveAction.canceled -= OnFlight2DMoveCanceled;
    }
}
