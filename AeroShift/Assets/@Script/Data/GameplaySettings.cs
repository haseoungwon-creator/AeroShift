using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CameraControllerSettings : JsonSettingsData
{
    public CameraControllerSettings() { configType = "CameraControllerSettings"; }

    public Vector3 cameraOffset3D = new Vector3(0.0f, 8.0f, -12.0f);
    public Vector3 cameraRotation3D = new Vector3(20.0f, 0.0f, 0.0f);
    public Vector3 cameraOffset2D = new Vector3(0.0f, 75.0f, 0.0f);
    public Vector3 cameraRotation2D = new Vector3(90.0f, 0.0f, 0.0f);
    public float orthographicSize2D = 48.0f;
    public Vector3 transitionCameraPosition = new Vector3(0.0f, 62.5f, 0.0f);
    public float startMoveDuration = 2.0f;

    protected override void ValidateValues()
    {
        JsonSettingRules.Finite(cameraOffset3D.x, "cameraOffset3D.x");
        JsonSettingRules.Finite(cameraOffset3D.y, "cameraOffset3D.y");
        JsonSettingRules.Finite(cameraOffset3D.z, "cameraOffset3D.z");
        JsonSettingRules.Finite(cameraRotation3D.x, "cameraRotation3D.x");
        JsonSettingRules.Finite(cameraRotation3D.y, "cameraRotation3D.y");
        JsonSettingRules.Finite(cameraRotation3D.z, "cameraRotation3D.z");
        JsonSettingRules.Finite(cameraOffset2D.x, "cameraOffset2D.x");
        JsonSettingRules.Finite(cameraOffset2D.y, "cameraOffset2D.y");
        JsonSettingRules.Finite(cameraOffset2D.z, "cameraOffset2D.z");
        JsonSettingRules.Finite(cameraRotation2D.x, "cameraRotation2D.x");
        JsonSettingRules.Finite(cameraRotation2D.y, "cameraRotation2D.y");
        JsonSettingRules.Finite(cameraRotation2D.z, "cameraRotation2D.z");
        JsonSettingRules.Finite(orthographicSize2D, "orthographicSize2D");
        JsonSettingRules.Require(orthographicSize2D > 0f, "orthographicSize2D must be positive");
        JsonSettingRules.Require(orthographicSize2D >= 1f, "orthographicSize2D");
        JsonSettingRules.Finite(transitionCameraPosition.x, "transitionCameraPosition.x");
        JsonSettingRules.Finite(transitionCameraPosition.y, "transitionCameraPosition.y");
        JsonSettingRules.Finite(transitionCameraPosition.z, "transitionCameraPosition.z");
        JsonSettingRules.Finite(startMoveDuration, "startMoveDuration");
        JsonSettingRules.Require(startMoveDuration >= 0f, "startMoveDuration must not be negative");
    }
}

[Serializable]
public sealed class MapManagerSettings : JsonSettingsData
{
    public MapManagerSettings() { configType = "MapManagerSettings"; }

    public float chunkLength = 80.0f;
    public Vector2 mapOriginXY = new Vector2(0.0f, 0.0f);
    public int forwardChunkCount = 6;
    public int startingSafeChunkCount = 3;
    public float keepBehindDistance = 160.0f;
    public int maxSpawnsPerFrame = 2;
    public int prepareLookaheadChunks = 4;
    public int transitionSafeChunkCount = 3;
    public bool spawnTransitionWall = true;
    public float transitionWallOffset = 30.0f;
    public bool enableProximityCollisionCulling = true;
    public int obstacleCollisionLayers = 1;
    public float collisionEnableAhead = 160.0f;
    public float collisionKeepBehind = 80.0f;
    public float collisionCheckInterval = 0.1f;
    public bool allow180DegreeRotation = true;

    protected override void ValidateValues()
    {
        JsonSettingRules.Finite(chunkLength, "chunkLength");
        JsonSettingRules.Require(chunkLength > 0f, "chunkLength must be positive");
        JsonSettingRules.Finite(mapOriginXY.x, "mapOriginXY.x");
        JsonSettingRules.Finite(mapOriginXY.y, "mapOriginXY.y");
        JsonSettingRules.Require(forwardChunkCount >= 1, "forwardChunkCount");
        JsonSettingRules.Require(startingSafeChunkCount >= 0, "startingSafeChunkCount");
        JsonSettingRules.Finite(keepBehindDistance, "keepBehindDistance");
        JsonSettingRules.Require(keepBehindDistance >= 0f, "keepBehindDistance must not be negative");
        JsonSettingRules.Require(maxSpawnsPerFrame >= 1, "maxSpawnsPerFrame");
        JsonSettingRules.Require(prepareLookaheadChunks >= 0, "prepareLookaheadChunks");
        JsonSettingRules.Require(transitionSafeChunkCount >= 0, "transitionSafeChunkCount");
        JsonSettingRules.Finite(transitionWallOffset, "transitionWallOffset");
        JsonSettingRules.Finite(collisionEnableAhead, "collisionEnableAhead");
        JsonSettingRules.Require(collisionEnableAhead >= 0f, "collisionEnableAhead must not be negative");
        JsonSettingRules.Finite(collisionKeepBehind, "collisionKeepBehind");
        JsonSettingRules.Require(collisionKeepBehind >= 0f, "collisionKeepBehind must not be negative");
        JsonSettingRules.Finite(collisionCheckInterval, "collisionCheckInterval");
        JsonSettingRules.Require(collisionCheckInterval > 0f, "collisionCheckInterval must be positive");
    }
}

[Serializable]
public sealed class MissileSpawnerSettings : JsonSettingsData
{
    public MissileSpawnerSettings() { configType = "MissileSpawnerSettings"; }

    public float minSpawnInterval = 0.5f;
    public float maxSpawnInterval = 1.0f;
    public int minMissileCount = 2;
    public int maxMissileCount = 4;
    public float spawnWidth = 160.0f;
    public float spawnHeight = 90.0f;
    public float spawnDistance = 50.0f;
    public float warningDuration = 2.0f;
    public int transitionMissilesPerSide = 5;

    protected override void ValidateValues()
    {
        JsonSettingRules.Finite(minSpawnInterval, "minSpawnInterval");
        JsonSettingRules.Require(minSpawnInterval > 0f, "minSpawnInterval must be positive");
        JsonSettingRules.Finite(maxSpawnInterval, "maxSpawnInterval");
        JsonSettingRules.Require(maxSpawnInterval > 0f, "maxSpawnInterval must be positive");
        JsonSettingRules.Require(minMissileCount >= 1, "minMissileCount");
        JsonSettingRules.Require(maxMissileCount >= 1, "maxMissileCount");
        JsonSettingRules.Finite(spawnWidth, "spawnWidth");
        JsonSettingRules.Require(spawnWidth >= 0f, "spawnWidth must not be negative");
        JsonSettingRules.Finite(spawnHeight, "spawnHeight");
        JsonSettingRules.Require(spawnHeight >= 0f, "spawnHeight must not be negative");
        JsonSettingRules.Finite(spawnDistance, "spawnDistance");
        JsonSettingRules.Require(spawnDistance >= 0f, "spawnDistance must not be negative");
        JsonSettingRules.Finite(warningDuration, "warningDuration");
        JsonSettingRules.Require(warningDuration >= 0f, "warningDuration must not be negative");
        JsonSettingRules.Require(transitionMissilesPerSide >= 1, "transitionMissilesPerSide");
        JsonSettingRules.Ordered(minSpawnInterval, maxSpawnInterval, "spawn intervals");
        JsonSettingRules.Ordered(minMissileCount, maxMissileCount, "missile counts");
    }
}

[Serializable]
public sealed class PlayerTransitionMovementSettings : JsonSettingsData
{
    public PlayerTransitionMovementSettings() { configType = "PlayerTransitionMovementSettings"; }

    public float transitionHeight = 150.0f;
    public float transitionDuration = 1.5f;
    public float baseWorldSpeed = 30.0f;
    public float riseRotationX = -70.0f;
    public float diveRotationX = 70.0f;
    public float diveTargetY = 50.0f;
    public float diveSpeed = 100.0f;
    public float diveRotationSpeed = 80.0f;
    public float return3DHeight = 10.0f;
    public float riseSpeedMultiplier = 1.5f;
    public float diveFinishDuration = 0.35f;

    protected override void ValidateValues()
    {
        JsonSettingRules.Finite(transitionHeight, "transitionHeight");
        JsonSettingRules.Require(transitionHeight >= 0f, "transitionHeight must not be negative");
        JsonSettingRules.Finite(transitionDuration, "transitionDuration");
        JsonSettingRules.Require(transitionDuration > 0f, "transitionDuration must be positive");
        JsonSettingRules.Finite(baseWorldSpeed, "baseWorldSpeed");
        JsonSettingRules.Require(baseWorldSpeed > 0f, "baseWorldSpeed must be positive");
        JsonSettingRules.Finite(riseRotationX, "riseRotationX");
        JsonSettingRules.Finite(diveRotationX, "diveRotationX");
        JsonSettingRules.Finite(diveTargetY, "diveTargetY");
        JsonSettingRules.Finite(diveSpeed, "diveSpeed");
        JsonSettingRules.Require(diveSpeed > 0f, "diveSpeed must be positive");
        JsonSettingRules.Require(diveSpeed >= 1f, "diveSpeed");
        JsonSettingRules.Finite(diveRotationSpeed, "diveRotationSpeed");
        JsonSettingRules.Require(diveRotationSpeed > 0f, "diveRotationSpeed must be positive");
        JsonSettingRules.Require(diveRotationSpeed >= 1f, "diveRotationSpeed");
        JsonSettingRules.Finite(return3DHeight, "return3DHeight");
        JsonSettingRules.Finite(riseSpeedMultiplier, "riseSpeedMultiplier");
        JsonSettingRules.Require(riseSpeedMultiplier > 0f, "riseSpeedMultiplier must be positive");
        JsonSettingRules.Finite(diveFinishDuration, "diveFinishDuration");
        JsonSettingRules.Require(diveFinishDuration > 0f, "diveFinishDuration must be positive");
        JsonSettingRules.Require(diveFinishDuration >= 0.01f, "diveFinishDuration");
    }
}

[Serializable]
public sealed class PoolManagerSettings : JsonSettingsData
{
    public PoolManagerSettings() { configType = "PoolManagerSettings"; }

    public PoolGroupSettings[] groups = new PoolGroupSettings[]
    {
        new PoolGroupSettings { id = PoolId.Cloud, resourceInitialCount = 192, resourceExpandWhenEmpty = true, initializeOnFirstUse = false, enableAutoShrink = true, idleSecondsBeforeShrink = 6, shrinkCheckIntervalSeconds = 2.0f },
        new PoolGroupSettings { id = PoolId.SafeMap, resourceInitialCount = 3, resourceExpandWhenEmpty = true, initializeOnFirstUse = false, enableAutoShrink = false, idleSecondsBeforeShrink = 60, shrinkCheckIntervalSeconds = 10.0f },
        new PoolGroupSettings { id = PoolId.NormalMap, resourceInitialCount = 14, resourceExpandWhenEmpty = true, initializeOnFirstUse = false, enableAutoShrink = true, idleSecondsBeforeShrink = 10, shrinkCheckIntervalSeconds = 5.0f },
        new PoolGroupSettings { id = PoolId.TransitionWall, resourceInitialCount = 1, resourceExpandWhenEmpty = true, initializeOnFirstUse = false, enableAutoShrink = false, idleSecondsBeforeShrink = 60, shrinkCheckIntervalSeconds = 10.0f },
        new PoolGroupSettings { id = PoolId.Missile, resourceInitialCount = 20, resourceExpandWhenEmpty = true, initializeOnFirstUse = true, enableAutoShrink = false, idleSecondsBeforeShrink = 30, shrinkCheckIntervalSeconds = 1.0f },
        new PoolGroupSettings { id = PoolId.MissileWarning, resourceInitialCount = 30, resourceExpandWhenEmpty = true, initializeOnFirstUse = true, enableAutoShrink = false, idleSecondsBeforeShrink = 30, shrinkCheckIntervalSeconds = 1.0f },
        new PoolGroupSettings { id = PoolId.MapStructure, resourceInitialCount = 100, resourceExpandWhenEmpty = true, initializeOnFirstUse = false, enableAutoShrink = false, idleSecondsBeforeShrink = 60, shrinkCheckIntervalSeconds = 5.0f },
    };
    public bool shrinkDuringGameplay = false;
    public int prewarmObstacleLayers = 1;

    protected override void ValidateValues()
    {
        JsonSettingRules.Require(groups != null && groups.Length > 0, "groups");
        var ids = new HashSet<PoolId>();
        for (int i = 0; i < groups.Length; i++)
        {
            JsonSettingRules.Require(groups[i] != null, "groups contains null");
            groups[i].Validate();
            JsonSettingRules.Require(ids.Add(groups[i].id), "duplicate pool ID");
        }
    }
}

[Serializable]
public sealed class PoolGroupSettings
{
    public PoolId id;
    public int resourceInitialCount;
    public bool resourceExpandWhenEmpty = true;
    public bool initializeOnFirstUse;
    public bool enableAutoShrink;
    public float idleSecondsBeforeShrink = 60f;
    public float shrinkCheckIntervalSeconds = 10f;

    public void Validate()
    {
        JsonSettingRules.Require(Enum.IsDefined(typeof(PoolId), id), "pool ID");
        JsonSettingRules.Require(resourceInitialCount >= 0, "resourceInitialCount");
        JsonSettingRules.Finite(idleSecondsBeforeShrink, "idleSecondsBeforeShrink");
        JsonSettingRules.Finite(shrinkCheckIntervalSeconds, "shrinkCheckIntervalSeconds");
        JsonSettingRules.Require(idleSecondsBeforeShrink >= 0f && shrinkCheckIntervalSeconds > 0f, "shrink timing");
    }
}

[Serializable]
public sealed class WorldMovementSettings : JsonSettingsData
{
    public WorldMovementSettings() { configType = "WorldMovementSettings"; }

    public float worldSpeed = 30.0f;
    public float maxWorldSpeed = 60.0f;
    public float speedIncrease = 5.0f;
    public float speedIncreaseInterval = 10.0f;

    protected override void ValidateValues()
    {
        JsonSettingRules.Finite(worldSpeed, "worldSpeed");
        JsonSettingRules.Require(worldSpeed >= 0f, "worldSpeed must not be negative");
        JsonSettingRules.Finite(maxWorldSpeed, "maxWorldSpeed");
        JsonSettingRules.Require(maxWorldSpeed >= 0f, "maxWorldSpeed must not be negative");
        JsonSettingRules.Finite(speedIncrease, "speedIncrease");
        JsonSettingRules.Require(speedIncrease >= 0f, "speedIncrease must not be negative");
        JsonSettingRules.Finite(speedIncreaseInterval, "speedIncreaseInterval");
        JsonSettingRules.Require(speedIncreaseInterval > 0f, "speedIncreaseInterval must be positive");
        JsonSettingRules.Ordered(worldSpeed, maxWorldSpeed, "worldSpeed / maxWorldSpeed");
    }
}
