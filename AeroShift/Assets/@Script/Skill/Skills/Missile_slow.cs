using System.Collections;
using System.Xml.Serialization;
using UnityEngine;

public class Missile_slow : MonoBehaviour
{
    [SerializeField] PoolManager poolManager;
    [SerializeField] MissileSpawner missileSpawner;
    [SerializeField] PlayerMode playerMode;
    [SerializeField] float duration = 5f;

    private float currentMultiplier;
    private bool isActive;
    private bool hasUsed;

    private void OnEnable()
    {
        if(missileSpawner != null)
        {
            missileSpawner.MissileSpawned += OnMissilespawned;
        }
    }

    private void OnDisable()
    {
        if(missileSpawner != null)
        {
            missileSpawner.MissileSpawned -= OnMissilespawned;
        }
        ResetSkill();
    }
    public void Activate(float multiplier)
    {
        Debug.Log("w");
        if (isActive || hasUsed || !playerMode.IsMode2D() || GameManager.Instance.CurrentState != GameStates.Playing)
            return;
        Debug.Log("f");
        hasUsed = true;
        isActive = true;
        currentMultiplier = multiplier;

        ApplyToActiveMissiles();
        StartCoroutine(SlowSequence());
    }

    private IEnumerator SlowSequence()
    {
        yield return new WaitForSeconds(duration);

        RestoreSpeed();
    }

    private void ApplyToActiveMissiles()
    {
        if(poolManager != null)
        {
            poolManager.ForEachActive<Missile>(PoolId.Missile, Applyspeed);
        }
    }
    private void Applyspeed(Missile missile)
    {
        if(missile != null)
        {
            missile.SetSpeedMultiplier(currentMultiplier);
        }
    }

    private void OnMissilespawned(Missile missile)
    {
        if(isActive && missile != null)
        {
            missile.SetSpeedMultiplier(currentMultiplier);
        }
    }
    private void RestoreSpeed()
    {
        isActive = false;
        currentMultiplier = 1f;
        ApplyToActiveMissiles();
    }

    public void ResetSkill()
    {
        StopAllCoroutines();
        RestoreSpeed();
        hasUsed = false;
    }

}
