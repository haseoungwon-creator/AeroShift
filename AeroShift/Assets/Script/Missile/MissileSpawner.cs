using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class MissileSpawner : MonoBehaviour
{
    [SerializeField] MissilePool missilePool;
    [SerializeField] MissileWarningPool warningPool;
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

    private List<Vector3> transitionSpawnPositions = new List<Vector3>();
    private List<Vector3> transitionDirections = new List<Vector3>();

    private void Awake()
    {
        enabled = false;
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
            warningPool.ReturnAllWarnings();
            missilePool.ReturnAllMissiles();
        }
        enabled = value;
        if (value)
            ResetSpawner();
    }

    public void StopSpawning()
    {
        StopAllCoroutines();
        enabled = false;
        warningPool.ReturnAllWarnings();
        missilePool.ReturnAllMissiles();
    }

    private void SetNextSpawnTime()
    {
        nextSpawnTime = Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    private void SpawnMissiles()
    {
        int missileCount = Random.Range(minMissileCount, maxMissileCount + 1);
        for(int i = 0; i < missileCount; i++)
        {
            StartCoroutine(SpawnMissile());
        }
    }

    private IEnumerator SpawnMissile()
    {
        Vector3 direction = GetRandomDirection();
        Vector3 spawnPosition = GetSpawnPosition(direction);

        GameObject warningObject = warningPool.GetWarning();

        if (warningObject == null)
            yield break;

        MissileWarning warning = warningObject.GetComponent<MissileWarning>();

        if (warning == null)
        {
            warningPool.ReturnWarning(warningObject);
            yield break;
        }

        warning.Initialize(warningPool);

        Vector2 screenPosition = GetWarningScreenPosition(spawnPosition,direction);
        warning.Show(screenPosition,direction);

        yield return new WaitForSeconds(warningDuration);

        GameObject missileObject = missilePool.GetMissile();

        if (missileObject != null)
        {
            float travelDistance = GetTravelDistance(direction);

            missileObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.FromToRotation(Vector3.up, direction));

            Missile missile = missileObject.GetComponent<Missile>();
            missile.Initialize(missilePool, direction, travelDistance);
        }
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

        if(direction == Vector3.back)
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

        yield return new WaitForSeconds(warningDuration);

        SpawnTransitionMissiles();
    }
    private void SpawnTransitionWarnings(Vector3 direction)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 spawnPosition = GetSpawnPosition(direction);

            transitionSpawnPositions.Add(spawnPosition);
            transitionDirections.Add(direction);

            GameObject warningObject = warningPool.GetWarning();

            if (warningObject == null)
                continue;

            MissileWarning warning = warningObject.GetComponent<MissileWarning>();

            if (warning == null)
            {
                warningPool.ReturnWarning(warningObject);
                continue;
            }

            warning.Initialize(warningPool);

            Vector2 screenPosition = GetWarningScreenPosition(spawnPosition, direction);
            warning.Show(screenPosition, direction);
        }
    }

    private IEnumerator WaitForAllMissilesReturned()
    {
        while (missilePool.HasActiveMissiles())
        {
            yield return null;
        }
    }

    private void SpawnTransitionMissiles()
    {
        for (int i = 0; i < transitionSpawnPositions.Count; i++)
        {
            GameObject missileObject = missilePool.GetMissile();

            if (missileObject == null)
                continue;

            Vector3 spawnPosition = transitionSpawnPositions[i];
            Vector3 direction = transitionDirections[i];
            float travelDistance = GetTravelDistance(direction);

            missileObject.transform.SetPositionAndRotation(
                spawnPosition,
                Quaternion.FromToRotation(Vector3.up, direction));

            Missile missile = missileObject.GetComponent<Missile>();

            if (missile != null)
                missile.Initialize(missilePool, direction, travelDistance);
        }
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

        Vector3 screenPosition = Camera.main.WorldToScreenPoint(edgePosition);

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
