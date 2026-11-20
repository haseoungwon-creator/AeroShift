using UnityEngine;

public class Shield : MonoBehaviour
{
    [SerializeField] GameObject shieldEffect;

    private int remainingHits;
    private bool hasUsed;
    private float protectionEndTime = -1f;
    private GameObject shieldeffect;
    private GameObject player;

    private void Awake()
    {
        player = GameObject.FindWithTag("Player");
    }
    public void Activate()
    {
        if (hasUsed)
            return;

        if (shieldeffect == null && shieldEffect != null)
        {
            if (player == null)
                player = GameObject.FindWithTag("Player");

            if (player != null)
            {
                shieldeffect = Instantiate(shieldEffect, player.transform);
                shieldeffect.transform.localScale = new Vector3(2f, 2f, 2f);
            }
        }

        hasUsed = true;
        remainingHits = 1;
        protectionEndTime = -1f;

        SetEffectActive(true);
    }

    public bool TryBlockHit()
    {
        if (Time.time < protectionEndTime)
            return true;

        if (remainingHits <= 0)
            return false;

        remainingHits--;

        if (remainingHits == 0)
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
        if (shieldeffect != null)
            shieldeffect.SetActive(active);
    }

    private void OnDisable()
    {
        ResetSkill();
    }

    private void OnDestroy()
    {
        if (shieldeffect != null)
            Destroy(shieldeffect);
    }
}
