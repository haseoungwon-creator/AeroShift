using System.Collections;
using UnityEngine;

public class PlayerCollision : MonoBehaviour
{
    [SerializeField] private LayerMask obstacleLayers = 1;
    [SerializeField] private PlayerRewind playerRewind;
    [SerializeField] private Shield shield;
    [SerializeField] private PhaseDash phaseDash;
    private Renderer objRender;

    private void Awake()
    {
        if (playerRewind == null)
            playerRewind = GetComponentInParent<PlayerRewind>();

        if (shield == null)
            shield = GetComponentInParent<Shield>();

        if (phaseDash == null)
            phaseDash = GetComponentInParent<PhaseDash>();

        if (objRender == null)
            objRender = GetComponentInChildren<Renderer>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        GameManager gameManager = GameManager.Instance;

        if (gameManager == null || gameManager.CurrentState != GameStates.Playing)
            return;

        if (playerRewind != null && playerRewind.IsRewinding)
            return;

        int collisionLayer = collision.collider.gameObject.layer;

        if ((obstacleLayers.value & (1 << collisionLayer)) == 0)
            return;

        if (phaseDash != null && phaseDash.IsProtected)
            return;

        if (shield != null && shield.TryBlockHit())
        {
            if (objRender != null)
            {
                StopAllCoroutines();
                StartCoroutine(BlinkRoutine(2f, 0.2f));
            }
            return;
        }

        gameManager.GameOver();
    }

    public IEnumerator BlinkRoutine(float duration, float interval)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            objRender.enabled = !objRender.enabled;

            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        objRender.enabled = true;

    }
}
