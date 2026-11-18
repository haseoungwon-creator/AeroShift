using UnityEngine;

public class PhaseDash : MonoBehaviour
{
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] float dashDuration = 0.2f;

    private bool hasUsed;
    private bool isDashing;
    private float dashEndTime;
    private float protectionEndTime = -1f;

    public bool IsDashing
    {
        get { return isDashing; }
    }

    public bool IsProtected
    {
        get { return isDashing || Time.time < protectionEndTime; }
    }

    public bool HasUsed
    {
        get { return hasUsed; }
    }

    private void Awake()
    {
        if (worldMovement == null)
            worldMovement = FindFirstObjectByType<WorldMovement>();
    }

    private void Update()
    {
        if (!isDashing)
            return;

        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameStates.Playing)
        {
            EndDash(false);
            return;
        }

        if (Time.time >= dashEndTime)
            EndDash(true);
    }

    public void Activate(float distance)
    {
        if (hasUsed || isDashing || !isActiveAndEnabled)
            return;

        if (worldMovement == null || distance <= 0f || dashDuration <= 0f)
            return;

        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameStates.Playing)
            return;

        hasUsed = true;
        isDashing = true;
        dashEndTime = Time.time + dashDuration;
        protectionEndTime = -1f;

        worldMovement.SetDashSpeedBonus(distance / dashDuration);
    }

    private void EndDash(bool completed)
    {
        isDashing = false;

        if (worldMovement != null)
            worldMovement.SetDashSpeedBonus(0f);

        if (completed)
            protectionEndTime = Time.time + 0.2f;
    }

    public void ResetSkill()
    {
        isDashing = false;
        hasUsed = false;
        dashEndTime = 0f;
        protectionEndTime = -1f;

        if (worldMovement != null)
            worldMovement.SetDashSpeedBonus(0f);
    }

    private void OnDisable()
    {
        ResetSkill();
    }
}
