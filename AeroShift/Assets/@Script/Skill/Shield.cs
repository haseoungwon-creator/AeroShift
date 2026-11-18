using UnityEngine;

public class Shield : MonoBehaviour
{
    [SerializeField] GameObject shieldEffect;

    private int remainingHits;
    private bool hasUsed;
    private float protectionEndTime = -1f;

    private void Awake()
    {
        SetEffectActive(false);
    }

    public void Activate()
    {
        if (hasUsed) return;

        hasUsed = true;
        remainingHits = 1;
        protectionEndTime = -1f;
        SetEffectActive(true);
    }
    public bool TryBlockHit()
    {
        if(Time.time < protectionEndTime)
        {
            return true;
        }

        if(remainingHits <= 0)
        {
            return false;
        }

        remainingHits--;

        if(remainingHits == 0)
        {
            protectionEndTime = Time.time + 0.2f;
            SetEffectActive(false);
        }

        return true;
    }

    public void ResetSkill()
    {
        remainingHits = 0;
        hasUsed = false;
        protectionEndTime = -1f;
        SetEffectActive(false);
    }
    private void SetEffectActive(bool active)
    {
        if(shieldEffect != null)
        {
            shieldEffect.SetActive(active);
        }
    }
}
