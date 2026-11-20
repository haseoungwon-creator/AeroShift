using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class MissileSpawner : MonoBehaviour
{
    [SerializeField] private PoolManager poolManager;
    [SerializeField] float minSpawnInterval = 2f;
    [SerializeField] float maxSpawnInterval = 4f;
    [SerializeField] int minMissileCount = 1;
    [SerializeField] int maxMissileCount = 3;
    [SerializeField] float spawnWidth = 80f;
    [SerializeField] float spawnHeight = 45f;
    [SerializeField] float spawnDistance = 20f;
    [SerializeField] float warningDuration = 2f;
    [SerializeField, Min(1)] private int transitionMissilesPerSide = 5;

    private struct TransitionShot
    {
        public Vector3 Position;
        public Vector3 Direction;

        public TransitionShot(Vector3 position, Vector3 direction)
        {
            Position = position;
            Direction = direction;
        }
    }

    private static readonly Vector3[] TransitionDirections =
    {
        Vector3.back,
        Vector3.forward,
        Vector3.left,
        Vector3.right
    };

    private readonly List<TransitionShot> transitionShots = new List<TransitionShot>(20);

    private float spawnTimer;
    private float nextSpawnTime;

    private WaitForSeconds warningWait;
    private Camera gameCamera;
    private bool poolsReady;
    private Transform flightPlaneTarget;

    private void Awake()
    {
        enabled = false;
        if (poolManager == null)
            poolManager = FindFirstObjectByType<PoolManager>();

        poolsReady = poolManager != null &&
            poolManager.IsGroupConfigured(PoolId.Missile) &&
            poolManager.IsGroupConfigured(PoolId.MissileWarning);

        warningWait = new WaitForSeconds(warningDuration);
        gameCamera = Camera.main;
    }

    public void SetFlightPlane(Transform player)
    {
        flightPlaneTarget = player;
        if (player == null)
            return;

        Vector3 position = transform.position;
        position.y = player.position.y;
        transform.position = position;
    }

    public bool PrewarmFor2D()
    {
        if (!poolsReady || poolManager == null)
            return false;

        bool missilesReady = poolManager.PrewarmInitialCount(PoolId.Missile);
        bool warningsReady = poolManager.PrewarmInitialCount(PoolId.MissileWarning);
        return missilesReady && warningsReady;
    }

    private void Update()
    {
        spawnTimer += Time.deltaTime;

        if (spawnTimer < nextSpawnTime)
            return;

        spawnTimer = 0f;
        SetNextSpawnTime();
        SpawnMissiles();
    }

    public void SetSpawning(bool value)
    {
        if (!value)
        {
            StopAllCoroutines();
            ReturnAllProjectiles();
        }

        enabled = value && poolsReady;
        if (enabled)
            ResetSpawner();
    }

    public void StopSpawning()
    {
        SetSpawning(false);
    }

    private void ReturnAllProjectiles()
    {
        if (poolManager == null)
            return;

        poolManager.ReturnAll(PoolId.MissileWarning);
        poolManager.ReturnAll(PoolId.Missile);
    }

    private void SetNextSpawnTime()
    {
        nextSpawnTime = Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    private void SpawnMissiles()
    {
        int missileCount = Random.Range(minMissileCount, maxMissileCount + 1);
        for (int i = 0; i < missileCount; i++)
        {
            StartCoroutine(SpawnMissile());
        }
    }

    private IEnumerator SpawnMissile()
    {
        Vector3 direction = GetRandomDirection();
        Vector3 spawnPosition = GetSpawnPosition(direction);

        if (!ShowWarning(spawnPosition, direction))
            yield break;

        yield return warningWait;
        SpawnMissileAt(spawnPosition, direction);
    }

    private bool ShowWarning(Vector3 spawnPosition, Vector3 direction)
    {
        GameObject warningObject = poolManager.RentInactive(
            PoolId.MissileWarning, Vector3.zero, Quaternion.identity, null);
        if (warningObject == null)
            return false;

        if (!warningObject.TryGetComponent(out MissileWarning warning))
        {
            poolManager.Return(warningObject);
            return false;
        }

        warning.Initialize(poolManager);
        warning.Show(GetWarningScreenPosition(spawnPosition, direction), direction);
        warningObject.SetActive(true);
        return true;
    }

    private void SpawnMissileAt(Vector3 spawnPosition, Vector3 direction,
        bool followFlightAltitude = false)
    {
        if (followFlightAltitude && flightPlaneTarget != null)
            spawnPosition.y = flightPlaneTarget.position.y;

        GameObject missileObject = poolManager.RentInactive(
            PoolId.Missile,
            spawnPosition,
            Quaternion.FromToRotation(Vector3.up, direction),
            null);
        if (missileObject == null)
            return;

        Missile missile = missileObject.GetComponentInChildren<Missile>(true);
        if (missile == null)
        {
            poolManager.Return(missileObject);
            return;
        }

        missile.Initialize(poolManager, missileObject, direction, GetTravelDistance(direction));
        if (followFlightAltitude)
            missile.FollowAltitude(flightPlaneTarget);

        missileObject.SetActive(true);
    }

    private Vector3 GetRandomDirection()
    {
        int direction = Random.Range(0, 4);

        switch (direction)
        {
            case 0:
                return Vector3.back;
            case 1:
                return Vector3.forward;
            case 2:
                return Vector3.left;
            default:
                return Vector3.right;
        }
    }

    private Vector3 GetSpawnPosition(Vector3 direction)
    {
        Vector3 center = transform.position;

        if (direction == Vector3.back)
        {
            float x = Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
            return center + new Vector3(x, 0f, spawnHeight * 0.5f + spawnDistance);
        }

        if (direction == Vector3.forward)
        {
            float x = Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);
            return center + new Vector3(x, 0f, -spawnHeight * 0.5f - spawnDistance);
        }

        if (direction == Vector3.left)
        {
            float z = Random.Range(-spawnHeight * 0.5f, spawnHeight * 0.5f);
            return center + new Vector3(spawnWidth * 0.5f + spawnDistance, 0f, z);
        }

        float randomz = Random.Range(-spawnHeight * 0.5f, spawnHeight * 0.5f);
        return center + new Vector3(-spawnWidth * 0.5f - spawnDistance, 0f, randomz);
    }

    private float GetTravelDistance(Vector3 direction)
    {
        if (direction == Vector3.left || direction == Vector3.right)
            return spawnWidth + spawnDistance * 2f;

        return spawnHeight + spawnDistance * 2f;
    }

    public IEnumerator SpawnTransitionPattern(System.Action onMissilesFired = null)
    {
        enabled = false;
        StopAllCoroutines();

        if (!poolsReady || poolManager == null)
            yield break;

        poolManager.ReturnAll(PoolId.MissileWarning);

        while (poolManager.GetActiveCount(PoolId.Missile) > 0)
        {
            if (!GameManager.IsPlaying)
            {
                ReturnAllProjectiles();
                yield break;
            }

            yield return null;
        }

        transitionShots.Clear();

        for (int side = 0; side < TransitionDirections.Length; side++)
        {
            Vector3 direction = TransitionDirections[side];

            for (int i = 0; i < transitionMissilesPerSide; i++)
            {
                if (!GameManager.IsPlaying)
                {
                    transitionShots.Clear();
                    ReturnAllProjectiles();
                    yield break;
                }

                Vector3 spawnPosition = GetSpawnPosition(direction);
                if (ShowWarning(spawnPosition, direction))
                    transitionShots.Add(new TransitionShot(spawnPosition, direction));
            }
        }

        if (transitionShots.Count == 0)
            yield break;

        yield return warningWait;

        if (!GameManager.IsPlaying)
        {
            transitionShots.Clear();
            ReturnAllProjectiles();
            yield break;
        }

        for (int i = 0; i < transitionShots.Count; i++)
        {
            if (!GameManager.IsPlaying)
            {
                transitionShots.Clear();
                ReturnAllProjectiles();
                yield break;
            }

            TransitionShot shot = transitionShots[i];
            SpawnMissileAt(shot.Position, shot.Direction, true);
        }

        transitionShots.Clear();

        onMissilesFired?.Invoke();

        while (poolManager.GetActiveCount(PoolId.Missile) > 0 ||
               poolManager.GetActiveCount(PoolId.MissileWarning) > 0)
        {
            if (!GameManager.IsPlaying)
            {
                ReturnAllProjectiles();
                yield break;
            }

            yield return null;
        }
    }

    public void ResetSpawner()
    {
        spawnTimer = 0f;
        SetNextSpawnTime();
    }

    private Vector2 GetWarningScreenPosition(Vector3 spawnPosition, Vector3 direction)
    {
        Vector3 center = transform.position;
        Vector3 edgePosition = spawnPosition;

        if (direction == Vector3.back)
            edgePosition.z = center.z + spawnHeight * 0.5f;
        else if (direction == Vector3.forward)
            edgePosition.z = center.z - spawnHeight * 0.5f;
        else if (direction == Vector3.left)
            edgePosition.x = center.x + spawnWidth * 0.5f;
        else
            edgePosition.x = center.x - spawnWidth * 0.5f;

        if (gameCamera == null)
            gameCamera = Camera.main;
        if (gameCamera == null)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Vector3 screenPosition = gameCamera.WorldToScreenPoint(edgePosition);

        float margin = 50f;

        if (direction == Vector3.back || direction == Vector3.forward)
        {
            screenPosition.x = Mathf.Clamp(screenPosition.x, margin, Screen.width - margin);
            screenPosition.y = direction == Vector3.back ? Screen.height - margin : margin;
        }
        else
        {
            screenPosition.x = direction == Vector3.left ? Screen.width - margin : margin;
            screenPosition.y = Mathf.Clamp(screenPosition.y, margin, Screen.height - margin);
        }

        return screenPosition;
    }

}
