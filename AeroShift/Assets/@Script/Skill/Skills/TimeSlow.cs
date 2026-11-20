using System.Collections;
using UnityEngine;

public class TimeSlow : MonoBehaviour
{
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] float duration = 5f;

    public bool CanUse
    {
        get { return !hasUsed && !isActive; }
    }

    private bool isActive;
    private bool hasUsed;

    public void Activate()
    {
        Activate(0.6f);
    }

    public void Activate(float multiplier)
    {
        if (!CanUse || multiplier <= 0f)
            return;

        if (!GameManager.IsPlaying)
            return;

        if (worldMovement == null)
            return;

        hasUsed = true;
        StartCoroutine(SlowSequence(multiplier));
    }

    private IEnumerator SlowSequence(float multiplier)
    {
        isActive = true;
        worldMovement.SetTemporarySpeedMultiplier(multiplier);

        yield return new WaitForSeconds(duration);

        if (worldMovement != null)
            worldMovement.SetTemporarySpeedMultiplier(1f);

        isActive = false;
    }

    public void ResetSkill()
    {
        StopAllCoroutines();

        isActive = false;
        hasUsed = false;

        if (worldMovement != null)
            worldMovement.SetTemporarySpeedMultiplier(1f);
    }
}
