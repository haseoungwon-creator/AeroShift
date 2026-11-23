
using System;
using UnityEngine;

[Serializable]
public sealed class MapGenerationSettingsData
{
    public int schemaVersion = 1;
    public float difficultyIntervalSeconds = 30f;
    public int maxDifficultyLevel = 5;
    public float minScale = 0.6f;
    public float maxScale = 1.4f;
    public float maxOverlapRatio = 0.1f;
    public float maxSafeZoneShiftRatio = 0.75f;
    public int placementAttemptLimit = 30;
    public int prepareLookaheadChunks = 3;
    public float areaMinX = -40f;
    public float areaMaxX = 40f;
    public float areaMinY = 5f;
    public float areaMaxY = 50f;
    public float stoneLocalZ = 0f;
    public float safeZoneClearance = 1f;
    public float playerMoveSpeedX = 25f;
    public float playerMoveSpeedY = 50f;
    public MapGenerationStoneTypeData[] stones =
        Array.Empty<MapGenerationStoneTypeData>();
    public MapGenerationDifficultyData[] levels =
        Array.Empty<MapGenerationDifficultyData>();
}

[Serializable]
public sealed class MapGenerationDifficultyData
{
    public int level;
    public float safeZoneSizeRatio;
    public int minStones;
    public int maxStones;
    public MapGenerationStoneWeight[] weights =
        Array.Empty<MapGenerationStoneWeight>();
}

[Serializable]
public sealed class MapGenerationStoneWeight
{
    public string prefabId;
    public int weight;
}

[Serializable]
public sealed class MapGenerationStoneTypeData
{
    public string prefabId;
    public float baseScale = 12f;
}
