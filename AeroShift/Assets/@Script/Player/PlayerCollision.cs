using UnityEngine;

public sealed class PlayerCollision : MonoBehaviour
{
    [SerializeField] private LayerMask obstacleLayers = 1;
    [SerializeField] private PlayerRewind playerRewind;

    private void Awake()
    {
        if (playerRewind == null)
            playerRewind = GetComponent<PlayerRewind>();
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

        gameManager.GameOver();
    }
}
