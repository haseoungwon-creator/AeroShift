using UnityEngine;
using System.Collections;

public class PlayerTransitionMovement : MonoBehaviour
{
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] float transitionHeight = 150f;
    [SerializeField] float transitionDuration = 1.5f;
    [SerializeField] float baseWorldSpeed = 30f;
    [SerializeField] float riseRotationX = -70f;
    [SerializeField] float diveRotationX = 70f;
    [SerializeField, Min(0f)] private float diveTargetY = 50f;
    [SerializeField] private float return3DHeight = 10f;
    [SerializeField] float riseSpeedMultiplier = 1.5f;
    [SerializeField, Min(0.01f)] private float diveFinishDuration = 0.35f;

    private PlayerMovement2D movement2D;

    private void Awake()
    {
        movement2D = GetComponent<PlayerMovement2D>();
    }

    public IEnumerator Rise()
    {
        return MoveVertically(true);
    }

    public IEnumerator Dive()
    {
        float duration = Mathf.Max(0.01f, GetTransitionDuration());
        float descentDistance = Mathf.Max(0f, transform.position.y - diveTargetY);
        float elapsed = 0f;
        float lastDistance = 0f;
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(
            diveRotationX, startRotation.eulerAngles.y, startRotation.eulerAngles.z);

        while (elapsed < duration)
        {
            if (!GameManager.IsPlaying)
                yield break;

            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float distance = descentDistance * progress;
            float delta = distance - lastDistance;
            lastDistance = distance;

            if (movement2D != null)
                movement2D.ApplyTransitionOffset(Vector3.down * delta);
            else
                transform.position += Vector3.down * delta;

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
            yield return null;
        }

        transform.rotation = targetRotation;
    }

    public IEnumerator FinishDive()
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(0f, return3DHeight, 10f);
        Quaternion initialRotation = transform.rotation;
        float elapsed = 0f;

        while (elapsed < diveFinishDuration)
        {
            if (!GameManager.IsPlaying)
                yield break;

            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / diveFinishDuration));

            transform.SetPositionAndRotation(
                Vector3.Lerp(start, end, progress),
                Quaternion.Slerp(initialRotation, Quaternion.identity, progress));
            yield return null;
        }

        transform.SetPositionAndRotation(end, Quaternion.identity);
    }

    private IEnumerator MoveVertically(bool rising)
    {
        float duration = GetTransitionDuration();
        if (rising)
            duration /= riseSpeedMultiplier;

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = rising
            ? startPosition + Vector3.up * transitionHeight
            : startPosition - Vector3.up * transitionHeight;

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(
            rising ? riseRotationX : diveRotationX,
            startRotation.eulerAngles.y,
            startRotation.eulerAngles.z);

        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / duration);

            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        transform.SetPositionAndRotation(
            rising ? new Vector3(0f, targetPosition.y, 0f) : new Vector3(0f, return3DHeight, 10f),
            rising ? Quaternion.identity : targetRotation);
    }

    private float GetTransitionDuration()
    {
        if (worldMovement == null || worldMovement.CurrentSpeed <= 0f)
            return transitionDuration;

        return transitionDuration * baseWorldSpeed / worldMovement.CurrentSpeed;
    }
}