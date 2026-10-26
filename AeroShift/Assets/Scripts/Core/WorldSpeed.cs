
using UnityEngine;

public class WorldSpeed : MonoBehaviour
{
    [SerializeField] private float baseSpeed = 30f;

    private float skinMultiplier = 1f;
    private float passiveMultiplier = 1f;
    private float slowMultiplier = 1f;

    public float CurrentSpeed =>
        baseSpeed * skinMultiplier * passiveMultiplier * slowMultiplier;

    public void SetBaseSpeed(float speed)
    {
        baseSpeed = Mathf.Max(0f, speed);
    }

    public void SetSkinMultiplier(float multiplier)
    {
        skinMultiplier = Mathf.Max(0f, multiplier);
    }

    public void SetPassiveMultiplier(float multiplier)
    {
        passiveMultiplier = Mathf.Max(0f, multiplier);
    }

    public void SetSlowMultiplier(float multiplier)
    {
        slowMultiplier = Mathf.Clamp01(multiplier);
    }

    public void ResetMultipliers()
    {
        skinMultiplier = 1f;
        passiveMultiplier = 1f;
        slowMultiplier = 1f;
    }
}
