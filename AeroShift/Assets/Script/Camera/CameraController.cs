using UnityEngine;
using System.Collections;

public class CameraController : MonoBehaviour
{
    [SerializeField] Transform player;
    [SerializeField] Vector3 cameraOffset3D = new Vector3(0, 8, -12);
    [SerializeField] Vector3 cameraRotation3D = new Vector3(20, 0, 0);
    [SerializeField] Vector3 cameraOffset2D = new Vector3(0, 90, 0);
    [SerializeField] Vector3 cameraRotation2D = new Vector3(90, 0, 0);
    [SerializeField] float startMoveDuration = 2f;

    private bool is2D;
    private bool isCameraLocked;
    private bool isStartPosition = true;
    private bool isMoving;

    private void Awake()
    {
        ResetCamera();
    }

    private void LateUpdate()
    {
        if (player == null || isMoving || isStartPosition)
            return;

        if (isCameraLocked)
        {
            FollowPlayer();
            return;
        }

        if (is2D)
            Update2DCamera();
        else
            Update3DCamera();
    }

    public void ResetCamera()
    {
        transform.SetPositionAndRotation(
            new Vector3(0f, 33.3f, -91.8f),
            Quaternion.identity);

        isStartPosition = true;
        isMoving = false;
        isCameraLocked = false;
        is2D = false;
    }

    public IEnumerator MoveToGamePosition()
    {
        if (player == null)
            yield break;

        isMoving = true;

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;

        Vector3 targetPosition =
            player.position + cameraOffset3D;

        Quaternion targetRotation =
            Quaternion.Euler(cameraRotation3D);

        float elapsedTime = 0f;

        while (elapsedTime < startMoveDuration)
        {
            elapsedTime += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsedTime / startMoveDuration);

            t = Mathf.SmoothStep(0f, 1f, t);

            transform.position =
                Vector3.Lerp(
                    startPosition,
                    targetPosition,
                    t);

            transform.rotation =
                Quaternion.Slerp(
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

    public IEnumerator MoveFromGamePosition()
    {
        isMoving = true;

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;

        Vector3 targetPosition =
            new Vector3(0f, 33.3f, -91.8f);

        Quaternion targetRotation =
            Quaternion.identity;

        float elapsedTime = 0f;

        while (elapsedTime < startMoveDuration)
        {
            elapsedTime += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsedTime / startMoveDuration);

            t = Mathf.SmoothStep(0f, 1f, t);

            transform.position =
                Vector3.Lerp(
                    startPosition,
                    targetPosition,
                    t);

            transform.rotation =
                Quaternion.Slerp(
                    startRotation,
                    targetRotation,
                    t);

            yield return null;
        }

        transform.SetPositionAndRotation(
            targetPosition,
            targetRotation);

        isStartPosition = true;
        isMoving = false;
    }

    public void Set3DView()
    {
        is2D = false;
        isCameraLocked = false;
        isStartPosition = false;

        Apply3DView();
    }

    public void Set2DView()
    {
        is2D = true;
        isCameraLocked = false;
        isStartPosition = false;

        Apply2DView();
    }

    private void Update3DCamera()
    {
        transform.position =
            player.position + cameraOffset3D;

        transform.rotation =
            Quaternion.Euler(cameraRotation3D);
    }

    private void Update2DCamera()
    {
        transform.position =
            new Vector3(0f, 90f, 0f);

        transform.rotation =
            Quaternion.Euler(cameraRotation2D);
    }

    private void FollowPlayer()
    {
        transform.LookAt(player);
    }

    public void LockCamera()
    {
        isCameraLocked = true;
    }

    private void Apply3DView()
    {
        transform.position =
            player.position + cameraOffset3D;

        transform.rotation =
            Quaternion.Euler(cameraRotation3D);
    }

    private void Apply2DView()
    {
        transform.position =
            new Vector3(0f, 90f, 0f);

        transform.rotation =
            Quaternion.Euler(cameraRotation2D);
    }
}