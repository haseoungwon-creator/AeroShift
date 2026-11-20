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
    [SerializeField] private float diveTargetY = -90f;
    [SerializeField, Min(1f)] private float diveSpeed = 100f;
    [SerializeField, Min(1f)] private float diveRotationSpeed = 80f;
    [SerializeField] private float return3DHeight = 10f;
    [SerializeField] float riseSpeedMultiplier = 1.5f;
    [SerializeField, Min(0.01f)] private float diveFinishDuration = 0.35f;

    public IEnumerator Rise()
    {
        return MoveVertically(true);
    }

   
    public IEnumerator Dive()
    {
        Quaternion targetRotation = Quaternion.Euler(
            diveRotationX,
            transform.eulerAngles.y,
            transform.eulerAngles.z);

        while (GameManager.IsPlaying)
        {
            Vector3 position = transform.position;
            if (position.y > diveTargetY)
                position.y = Mathf.MoveTowards(
                    position.y, diveTargetY, diveSpeed * Time.deltaTime);

            transform.position = position;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRotation,
                diveRotationSpeed * Time.deltaTime);

            if (position.y <= diveTargetY + 0.01f &&
                Quaternion.Angle(transform.rotation, targetRotation) < 0.1f)
                break;

            yield return null;
        }
    }
    public IEnumerator FinishDive()
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(0f, return3DHeight-90f, 10f);
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
            rising ? new Vector3(0f, 10f, 0f) : new Vector3(0f, return3DHeight, 10f),
            rising ? Quaternion.identity : targetRotation);
    }

    private float GetTransitionDuration()
    {
        if (worldMovement == null || worldMovement.CurrentSpeed <= 0f)
            return transitionDuration;

        return transitionDuration * baseWorldSpeed / worldMovement.CurrentSpeed;
    }
}