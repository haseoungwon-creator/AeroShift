using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    [SerializeField] MapPool mapPool;
    [SerializeField] WallPool wallPool;
    [SerializeField] Transform player;
    [SerializeField] float mapLength = 80f;
    [SerializeField] int forwardMapCount = 6;
    [SerializeField] int safeMapCount = 3;
    [SerializeField] float worldSpeed = 30f;
    [SerializeField] float keepBegindDistance = 250f;
    [SerializeField] WorldMovement worldMovement;
    [SerializeField] float wallDistance = 30f;
    [SerializeField] PlayerRewind playerRewind;


    private float transitionWallZ;
    private float transitionMapEndZ;
    private bool isTransitionMapActive;


    private readonly Queue<SpawnedMap> spawnedMaps = new Queue<SpawnedMap>();
    private GameObject farthestMap;
    private GameObject transitionSafeMap;
    int mapCount;

    private bool is3DMapActive = true;
    private struct SpawnedMap
    {
        public GameObject map;
        public int mapIndex;
        public bool isSafe;

        public SpawnedMap(GameObject map,int mapIndex,bool isSafe)
        {
            this.map = map;
            this.mapIndex = mapIndex;
            this.isSafe = isSafe;
        }
    }

    public bool IsTransitionMapArrived(float transitionMapEndZ)
    {
        if (transitionSafeMap == null) return false;

        float currentEndZ = transitionSafeMap.transform.position.z - mapLength * 0.5f;
        return currentEndZ <= player.position.z;
    }

    private void Update()
    {
        if (!is3DMapActive)
            return;

        if (playerRewind != null && playerRewind.IsRewinding)
            return;

        if (!isTransitionMapActive)
            SpawnMapsAhead();

        RemoveMapsBehind();
    }


    private void SpawnMapsAhead()
    {
        float requiredZ = player.position.z + mapLength * forwardMapCount;
        float forthestZ = farthestMap != null ? farthestMap.transform.position.z : -mapLength;
        forthestZ = Mathf.Max(forthestZ, transitionWallZ);

        int guard = 0;

        while (forthestZ < requiredZ)
        {
            float spawnZ = forthestZ + mapLength;

            if (farthestMap == null)
                spawnZ = 0f;

            if (!SpawnMap(spawnZ))
                break;

            forthestZ = spawnZ;

            if (++guard > 2000)
                break;
        }
    }

    private float GetFarthestZ()
    {
        return farthestMap != null ? farthestMap.transform.position.z : 0f;
    }

    private bool SpawnMap(float spawnZ)
    {
        bool isSafe = mapCount < safeMapCount;
        int mapIndex = isSafe ? -1 : Random.Range(0, mapPool.MapPrefabsCount);
        GameObject map = isSafe ? mapPool.GetSafeMap() : mapPool.GetMap(mapIndex);

        if(map == null)
            return false;

        Quaternion rotation = Quaternion.identity;

        if (!isSafe)
        {
            int rotationY = Random.Range(0, 2) == 0 ? 0 : 180;
            rotation = Quaternion.Euler(0f,rotationY, 0f);
        }

        map.transform.SetPositionAndRotation(new Vector3(0f, 0f, spawnZ), rotation);

        spawnedMaps.Enqueue(new SpawnedMap(map,mapIndex, isSafe));
        farthestMap = map;
        mapCount++;

        return true;
    }

    private void RemoveMapsBehind()
    {
        float deleteZ = player.position.z - keepBegindDistance;

        while(spawnedMaps.Count > 0)
        {
            SpawnedMap spawnedMap = spawnedMaps.Peek();
            GameObject map = spawnedMap.map;

            if(map == null)
            {
                spawnedMaps.Dequeue();
                continue;
            }

            float frontEdge = map.transform.position.z + mapLength * 0.5f;

            if (frontEdge >= deleteZ)
                break;

            if (map == farthestMap)
                farthestMap = null;

            if (spawnedMap.isSafe)
                mapPool.ReturnSafeMap(map);

            else
                mapPool.ReturnMap(spawnedMap.mapIndex,map);

            spawnedMaps.Dequeue();
        }
    }

    public void ResetMaps()
    {
        while (spawnedMaps.Count > 0)
        {
            SpawnedMap spawnedMap = spawnedMaps.Dequeue();

            if (spawnedMap.map == null)
                continue;

            if (spawnedMap.isSafe)
                mapPool.ReturnSafeMap(spawnedMap.map);
            else
                mapPool.ReturnMap(spawnedMap.mapIndex, spawnedMap.map);
        }

        wallPool.ReturnWall();

        transitionSafeMap = null;
        farthestMap = null;
        mapCount = 0;
        transitionWallZ = 0f;
        isTransitionMapActive = false;
        is3DMapActive = true;
    }

    public void HideMaps()
    {
        is3DMapActive = false;
        foreach(SpawnedMap spawnedmap in spawnedMaps)
        {
            if(spawnedmap.map == null) continue;

            spawnedmap.map.SetActive(false);
        }

        wallPool.ReturnWall();
    }

    private bool SpawnSafeMap(float spawnZ)
    {
        GameObject map = mapPool.GetSafeMap();
        if(map == null)
            return false;

        map.transform.SetPositionAndRotation(
            new Vector3(0,0,spawnZ),Quaternion.identity);

        spawnedMaps.Enqueue(new SpawnedMap(map, -1, true));
        farthestMap = map;
        mapCount++;

        return true;
    }

    public void SpawnTransitionSafeMaps()
    {
        isTransitionMapActive = true;

        float farthestz = GetFarthestZ();
        float spawnZ = farthestz + mapLength;

        if (!SpawnSafeMap(spawnZ))
            return;

        transitionSafeMap = farthestMap;

        float transitionMapEndZ = transitionSafeMap.transform.position.z + mapLength * 0.5f;
        transitionWallZ = transitionMapEndZ + wallDistance;

        SpawnWall(transitionWallZ);
    }

    public void EndTransitionMap()
    {
        isTransitionMapActive = false;
        transitionWallZ = 0f;
        transitionSafeMap = null;
    }

    
    private GameObject SpawnWall(float spawnZ)
    {
        GameObject wall = wallPool.GetWall();

        if(wall == null) return null;

        wall.transform.SetPositionAndRotation(
            new Vector3(0f,0f,spawnZ),Quaternion.identity);

        return wall;
    }
}
