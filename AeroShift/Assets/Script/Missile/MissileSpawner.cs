using System.Collections.Generic;
using System.Collections;
using UnityEngine;

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

    private float spawnTimer;
    private float nextSpawnTime;

    private readonly List<Vector3> transitionSpawnPositions = new List<Vector3>(20);
    private readonly List<Vector3> transitionDirections = new List<Vector3>(20);
    private WaitForSeconds warningWait;
    private Camera gameCamera;
    private bool poolsReady;

    private void Awake()
    {
        enabled = false;
        if (poolManager == null)
            poolManager = FindFirstObjectByType<PoolManager>();

        // Do not instantiate 2D-only prefabs during 3D startup.
        poolsReady = poolManager != null &&
            poolManager.IsGroupConfigured(PoolId.Missile) &&
            poolManager.IsGroupConfigured(PoolId.MissileWarning);

        if (!poolsReady)
            Debug.LogError("MissileSpawner: PoolManager의 Missile 및 MissileWarning 그룹을 확인하세요.", this);

        warningWait = new WaitForSeconds(warningDuration);
        gameCamera = Camera.main;
    }

    // Called every frame while the safe transition map approaches.
    // Prepares the two 2D groups incrementally before any missile can fire.
    public bool PrewarmFor2D()
    {
        if (!poolsReady || poolManager == null)
            return false;

        // Do not short-circuit: each group can prepare two objects per frame.
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
        StopAllCoroutines();
        enabled = false;
        ReturnAllProjectiles();
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

    private void SpawnMissileAt(Vector3 spawnPosition, Vector3 direction)
    {
        GameObject missileObject = poolManager.RentInactive(
            PoolId.Missile,
            spawnPosition,
            Quaternion.FromToRotation(Vector3.up, direction),
            null);
        if (missileObject == null)
            return;

        if (!missileObject.TryGetComponent(out Missile missile))
        {
            poolManager.Return(missileObject);
            return;
        }

        missile.Initialize(poolManager, direction, GetTravelDistance(direction));
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

    public void ResetSpawner()
    {
        spawnTimer = 0f;
        SetNextSpawnTime();
    }

    public IEnumerator SpawnTransitionPattern()
    {
        enabled = false;
        if (!poolsReady)
            yield break;

        transitionSpawnPositions.Clear();
        transitionDirections.Clear();

        yield return WaitForAllMissilesReturned();

        for (int i = 0; i < 4; i++)
        {
            Vector3 direction;

            switch (i)
            {
                case 0: direction = Vector3.back; break;
                case 1: direction = Vector3.forward; break;
                case 2: direction = Vector3.left; break;
                default: direction = Vector3.right; break;
            }

            SpawnTransitionWarnings(direction);
        }

        yield return warningWait;

        SpawnTransitionMissiles();
    }
    private void SpawnTransitionWarnings(Vector3 direction)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 spawnPosition = GetSpawnPosition(direction);

            transitionSpawnPositions.Add(spawnPosition);
            transitionDirections.Add(direction);
            ShowWarning(spawnPosition, direction);
        }
    }

    private IEnumerator WaitForAllMissilesReturned()
    {
        while (poolManager.GetActiveCount(PoolId.Missile) > 0)
        {
            yield return null;
        }
    }

    private void SpawnTransitionMissiles()
    {
        for (int i = 0; i < transitionSpawnPositions.Count; i++)
            SpawnMissileAt(transitionSpawnPositions[i], transitionDirections[i]);
    }

    private float GetTransitionPatternDuration()
    {
        float horizontalTime = (spawnWidth + spawnDistance * 2f) / 20f;
        float verticalTime = (spawnHeight + spawnDistance * 2f) / 20f;

        return Mathf.Max(horizontalTime, verticalTime);
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
