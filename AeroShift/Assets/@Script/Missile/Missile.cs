using UnityEngine;

public class Missile : MonoBehaviour
{
    [SerializeField] float speed = 100f;

    private PoolManager poolManager;
    private Vector3 direction;
    private float travelDistance;
    private float movedDistance;

    public void Initialize(PoolManager pool, Vector3 direction, float travelDistance)
    {
        poolManager = pool;
        this.direction = direction.normalized;
        this.travelDistance = travelDistance;
        movedDistance = 0f;
    }

    private void Update()
    {
        float moveDistance = speed * Time.deltaTime;
        transform.position += direction * moveDistance;
        movedDistance += moveDistance;

        if (movedDistance >= travelDistance)
            ReturnToPool();
    }

    public void ReturnToPool()
    {
        if (poolManager == null)
            return;

        poolManager.Return(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") && other.GetComponentInParent<PlayerController>() == null)
            return;

        PhaseDash phaseDash = other.GetComponentInParent<PhaseDash>();

        if (phaseDash != null && phaseDash.IsProtected)
        {
            ReturnToPool();
            return;
        }

        Shield shield = other.GetComponentInParent<Shield>();

        if (shield != null && shield.TryBlockHit())
        {
            ReturnToPool();
            return;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.GameOver();

        ReturnToPool();
    }
}
