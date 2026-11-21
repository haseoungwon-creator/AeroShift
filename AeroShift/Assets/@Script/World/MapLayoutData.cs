using System;
using UnityEngine;

// Read once at startup from Resources/Config/Maps/MapLayouts.json.
[Serializable]
public sealed class MapLayoutDatabase
{
    public int schemaVersion = 1;
    public MapLayoutDefinition[] maps = Array.Empty<MapLayoutDefinition>();
}

[Serializable]
public sealed class MapLayoutDefinition
{
    public string id;
    public MapStructurePlacement[] structures = Array.Empty<MapStructurePlacement>();
}

[Serializable]
public sealed class MapStructurePlacement
{
    // The name of a prefab in Resources/MapStructures, e.g. ST_Stone2.
    public string prefabId;
    public Vector3 position;
    public Vector3 rotation; // Euler angles, relative to the map chunk.
    public Vector3 scale = Vector3.one;
}
