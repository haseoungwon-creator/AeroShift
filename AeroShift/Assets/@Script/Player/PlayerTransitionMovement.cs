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

    public bool Istransitioning { get; private set; }

    public IEnumerator Rise()
    {
        Istransitioning = true;

        float duration = GetTransitionDuration() / riseSpeedMultiplier;

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + Vector3.up * transitionHeight;

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(riseRotationX, startRotation.eulerAngles.y, startRotation.eulerAngles.z);

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float t = elapsedTime / duration;
            t = Mathf.SmoothStep(0f, 1f, t);

            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }

        transform.position = new Vector3(0f, 10f, 0f);
        transform.rotation = Quaternion.Euler(0f, 0f, 0f);

        Istransitioning = false;
    }

    public IEnumerator Dive()
    {
        Istransitioning = true;

        float duration = GetTransitionDuration();

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition - Vector3.up * transitionHeight;

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(diveRotationX, startRotation.eulerAngles.y, startRotation.eulerAngles.z);

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float t = elapsedTime / duration;
            t = Mathf.SmoothStep(0f, 1f, t);

            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }

        transform.position = new Vector3(0f, 10f, 10f);
        transform.rotation = targetRotation;

        Istransitioning = false;
    }

    private float GetTransitionDuration()
    {
        if (worldMovement == null || worldMovement.CurrentSpeed <= 0f)
            return transitionDuration;

        return transitionDuration * baseWorldSpeed / worldMovement.CurrentSpeed;
    }
}