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
            playerRewind = FindAnyObjectByType<PlayerRewind>();

        if (shield == null)
            shield = FindAnyObjectByType<Shield>();

        if (phaseDash == null)
            phaseDash = FindAnyObjectByType<PhaseDash>();

        if (objRender == null)
            objRender = GetComponent<Renderer>();
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

        StartCoroutine(BlinkRoutine(2.0f, 0.2f));

        if (phaseDash != null && phaseDash.IsDashing)
            return;

        if (shield != null && shield.TryBlockHit())
            return;

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
