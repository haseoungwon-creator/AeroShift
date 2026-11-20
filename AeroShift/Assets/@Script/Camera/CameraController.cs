
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;

    [Header("3D Camera")]
    [SerializeField] private Vector3 cameraOffset3D = new Vector3(0f, 8f, -12f);
    [SerializeField] private Vector3 cameraRotation3D = new Vector3(20f, 0f, 0f);

    [Header("2D Camera")]
    [SerializeField] private Vector3 cameraOffset2D = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 cameraRotation2D = new Vector3(90f, 0f, 0f);
    [SerializeField, Min(1f)] private float orthographicSize2D = 48f;

    [Header("2D to 3D Transition")]
    [SerializeField] private Vector3 transitionCameraPosition = new Vector3(0f, 62.5f, 0f);

    [Header("Start Camera")]
    [SerializeField] private float startMoveDuration = 2f;

    private const float ReferenceAspect2D = 16f / 9f;

    private Camera gameCamera;
    private float defaultPerspectiveFov;
    private bool is2D;
    private bool isCameraLocked;
    private bool isStartPosition = true;
    private bool isMoving;
    private bool isTransitioningTo3D;
    private float fixed2DCameraY;

    private void Awake()
    {
        gameCamera = GetComponent<Camera>();
        defaultPerspectiveFov = gameCamera.fieldOfView;
        ResetCamera();
    }

    private void LateUpdate()
    {
        if (player == null || isMoving || isStartPosition)
            return;

        if (isCameraLocked)
        {
            transform.LookAt(player);
            return;
        }

        if (is2D)
            Apply2DView();
        else
            Apply3DView();
    }

    public void ResetCamera()
    {
        gameCamera.orthographic = false;
        gameCamera.fieldOfView = defaultPerspectiveFov;

        transform.SetPositionAndRotation(
            new Vector3(0f, 33.3f, -91.8f),
            Quaternion.identity);

        isStartPosition = true;
        isMoving = false;
        isCameraLocked = false;
        is2D = false;
        isTransitioningTo3D = false;
        fixed2DCameraY = transform.position.y;
    }

    public IEnumerator MoveToGamePosition()
    {
        if (player == null)
            yield break;

        gameCamera.orthographic = false;
        gameCamera.fieldOfView = defaultPerspectiveFov;
        isMoving = true;
        isTransitioningTo3D = false;

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Vector3 targetPosition = player.position + cameraOffset3D;
        Quaternion targetRotation = Quaternion.Euler(cameraRotation3D);

        float elapsedTime = 0f;

        while (elapsedTime < startMoveDuration)
        {
            elapsedTime += Time.deltaTime;

            float t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsedTime / startMoveDuration));

            transform.position = Vector3.Lerp(
                startPosition,
                targetPosition,
                t);

            transform.rotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                t);

            yield return null;
        }

        transform.SetPositionAndRotation(
            targetPosition,
            targetRotation);

        isStartPosition = false;
        isMoving = false;
    }

    // 2D에서 Shift 입력 시 호출
    public void SetPerspectiveFor3DTransition()
    {
        isTransitioningTo3D = true;
        isCameraLocked = false;
        isStartPosition = false;

        // 지정된 위치로 즉시 이동
        transform.SetPositionAndRotation(
            transitionCameraPosition,
            Quaternion.Euler(cameraRotation2D));

        // Orthographic과 Perspective의 화면 크기 차이 보정
        if (gameCamera.orthographic && player != null)
        {
            float planeDepth = Vector3.Dot(
                player.position - transform.position,
                transform.forward);

            if (planeDepth > gameCamera.nearClipPlane)
            {
                float halfAngle = Mathf.Atan(
                    gameCamera.orthographicSize / planeDepth);

                gameCamera.fieldOfView = Mathf.Clamp(
                    halfAngle * 2f * Mathf.Rad2Deg,
                    1f,
                    179f);
            }
            else
            {
                gameCamera.fieldOfView = defaultPerspectiveFov;
            }
        }

        gameCamera.orthographic = false;
    }

    public void Set3DView()
    {
        is2D = false;
        isTransitioningTo3D = false;
        isCameraLocked = false;
        isStartPosition = false;

        gameCamera.orthographic = false;
        gameCamera.fieldOfView = defaultPerspectiveFov;

        if (player != null)
            Apply3DView();
    }

    public void Set2DView()
    {
        is2D = true;
        isTransitioningTo3D = false;
        isCameraLocked = false;
        isStartPosition = false;

        gameCamera.orthographic = true;
        fixed2DCameraY = cameraOffset2D.y;

        if (player != null)
            Apply2DView();
    }

    public void LockCamera()
    {
        isCameraLocked = true;
    }

    private void Apply3DView()
    {
        transform.SetPositionAndRotation(
            player.position + cameraOffset3D,
            Quaternion.Euler(cameraRotation3D));
    }

    private void Apply2DView()
    {
        float aspect = Mathf.Max(0.01f, gameCamera.aspect);

        gameCamera.orthographicSize = orthographicSize2D *
            Mathf.Max(1f, ReferenceAspect2D / aspect);

        // 전환 중에는 카메라 위치를 고정
        Vector3 targetPosition = isTransitioningTo3D
            ? transitionCameraPosition
            : new Vector3(
                cameraOffset2D.x,
                fixed2DCameraY,
                cameraOffset2D.z);

        transform.SetPositionAndRotation(
            targetPosition,
            Quaternion.Euler(cameraRotation2D));
    }

    private void OnValidate()
    {
        orthographicSize2D = Mathf.Max(
            1f,
            orthographicSize2D);
    }
}
