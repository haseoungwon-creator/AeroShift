using UnityEngine;

public class Missile : MonoBehaviour
{
    [SerializeField] private float speed = 100f;

    private PoolManager poolManager;
    private GameObject pooledObject;
    private Transform movementTransform;
    private Vector3 direction;
    private float travelDistance;
    private float movedDistance;

    public void Initialize(PoolManager pool, GameObject poolRoot, Vector3 moveDirection, float maxDistance)
    {
        poolManager = pool;
        pooledObject = poolRoot;
        movementTransform = poolRoot != null ? poolRoot.transform : transform;
        direction = moveDirection.normalized;
        travelDistance = maxDistance;
        movedDistance = 0f;
    }

    private void Update()
    {
        if (poolManager == null || pooledObject == null)
            return;

        float moveDistance = speed * Time.deltaTime;
        movementTransform.position += direction * moveDistance;
        movedDistance += moveDistance;

        if (movedDistance >= travelDistance)
            ReturnToPool();
    }

    public void ReturnToPool()
    {
        if (poolManager != null && pooledObject != null)
            poolManager.Return(pooledObject);
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
