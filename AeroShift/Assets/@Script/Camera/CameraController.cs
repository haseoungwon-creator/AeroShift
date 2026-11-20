using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Vector3 cameraOffset3D = new Vector3(0f, 8f, -12f);
    [SerializeField] private Vector3 cameraRotation3D = new Vector3(20f, 0f, 0f);
    [SerializeField] private Vector3 cameraOffset2D = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 cameraRotation2D = new Vector3(90f, 0f, 0f);
    [SerializeField, Min(1f)] private float orthographicSize2D = 48f;
    [SerializeField] private float startMoveDuration = 2f;

    private const float ReferenceAspect2D = 16f / 9f;

    private Camera gameCamera;
    private bool is2D;
    private bool isCameraLocked;
    private bool isStartPosition = true;
    private bool isMoving;
    private float fixed2DCameraY;

    private void Awake()
    {
        gameCamera = GetComponent<Camera>();
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
        transform.SetPositionAndRotation(
            new Vector3(0f, 33.3f, -91.8f),
            Quaternion.identity);

        isStartPosition = true;
        isMoving = false;
        isCameraLocked = false;
        is2D = false;
        fixed2DCameraY = transform.position.y;
    }

    public IEnumerator MoveToGamePosition()
    {
        if (player == null)
            yield break;

        gameCamera.orthographic = false;
        isMoving = true;

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Vector3 targetPosition = player.position + cameraOffset3D;
        Quaternion targetRotation = Quaternion.Euler(cameraRotation3D);

        float elapsedTime = 0f;
        while (elapsedTime < startMoveDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsedTime / startMoveDuration));

            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        transform.SetPositionAndRotation(targetPosition, targetRotation);
        isStartPosition = false;
        isMoving = false;
    }
        
    public void SetPerspectiveFor3DTransition()
    {
        gameCamera.orthographic = false;
    }

    public void Set3DView()
    {
        is2D = false;
        isCameraLocked = false;
        isStartPosition = false;
        gameCamera.orthographic = false;
        if (player != null)
            Apply3DView();
    }

    public void Set2DView()
    {
        is2D = true;
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

        transform.SetPositionAndRotation(
            new Vector3(cameraOffset2D.x,
                fixed2DCameraY,
                cameraOffset2D.z),
            Quaternion.Euler(cameraRotation2D));
    }

    private void OnValidate()
    {
        orthographicSize2D = Mathf.Max(1f, orthographicSize2D);
    }
}
