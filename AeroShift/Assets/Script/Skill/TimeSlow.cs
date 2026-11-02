using System.Collections;
using UnityEngine;

public class TimeSlow : MonoBehaviour
{
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] float slowSpeed = 20f;
    [SerializeField] float duration = 5f;

    public bool IsActive => isActive;
    public bool HasUsed => hasUsed;
    public bool CanUse => !hasUsed && !isActive;

    private bool isActive;
    private bool hasUsed;
    private float originalSpeed;

    public void Activate()
    {
        if (!CanUse)
            return;

        if (GameManager.Instance == null)
            return;

        if (GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        if (worldMovement == null)
            return;

        hasUsed = true;

        StartCoroutine(SlowSequence());
    }

    private IEnumerator SlowSequence()
    {
        isActive = true;

        originalSpeed = worldMovement.CurrentSpeed;

        worldMovement.SetSpeed(slowSpeed);

        yield return new WaitForSeconds(duration);

        worldMovement.SetSpeed(originalSpeed);

        isActive = false;
    }

    public void ResetSkill()
    {
        StopAllCoroutines();

        isActive = false;
        hasUsed = false;

        if (worldMovement != null)
            worldMovement.SetSpeed(originalSpeed);
    }
}