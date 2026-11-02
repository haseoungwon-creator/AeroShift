using UnityEngine;

public class Missile : MonoBehaviour
{
    [SerializeField] float speed = 100f;

    private MissilePool missilePool;
    private Vector3 direction;
    private float travelDistance;
    private float movedDistance;

    public void Initialize(MissilePool pool,Vector3 direction,float travelDistance)
    {
        missilePool = pool;
        this.direction = direction.normalized;
        this.travelDistance = travelDistance;
        movedDistance = 0f;
    }

    private void Update()
    {
        float moveDistance = speed * Time.deltaTime;
        transform.position += direction * moveDistance;
        movedDistance += moveDistance;

        if(movedDistance >= travelDistance)
            ReturnToPool();
    }

    public void ReturnToPool()
    {
        if (missilePool == null)
            return;

        missilePool.ReturnMissile(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        GameManager.Instance.GameOver();
        ReturnToPool();
    }
}
