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
    [SerializeField] float riseSpeedMultiplier = 1.5f;

    public IEnumerator Rise()
    {
        return MoveVertically(true);
    }

    public IEnumerator Dive()
    {
        return MoveVertically(false);
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
            rising ? new Vector3(0f, 10f, 0f) : new Vector3(0f, 10f, 10f),
            rising ? Quaternion.identity : targetRotation);
    }

    private float GetTransitionDuration()
    {
        if (worldMovement == null || worldMovement.CurrentSpeed <= 0f)
            return transitionDuration;

        return transitionDuration * baseWorldSpeed / worldMovement.CurrentSpeed;
    }
}