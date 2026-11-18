using UnityEngine;

public class Shield : MonoBehaviour
{
    [SerializeField] GameObject shieldEffect;

    private int remainingHits;
    private bool hasUsed;
    private float protectionEndTime = -1f;
    GameObject shieldeffect;
    GameObject player;

    private void Start()
    {
        player = GameObject.FindWithTag("Player");
    }
    public void Activate()
    {
        if (hasUsed)
            return;

        shieldeffect = Instantiate(shieldEffect, player.transform);
        shieldeffect.transform.localScale = new Vector3(10f, 10f, 10f);

        hasUsed = true;
        remainingHits = 1;
        protectionEndTime = -1f;
        
        SetEffectActive(shieldeffect,true);
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
            SetEffectActive(shieldeffect,false);
        }

        return true;
    }

    public void ResetSkill()
    {
        remainingHits = 0;
        hasUsed = false;
        protectionEndTime = -1f;
        SetEffectActive(shieldeffect,false);
    }

    private void SetEffectActive(GameObject effect, bool active)
    {
        if (effect != null)
            effect.SetActive(active);
    }
}
