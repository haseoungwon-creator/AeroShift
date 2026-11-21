using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-600)]
public class ResourceManager : MonoBehaviour
{
    [Serializable]
    public class ResourceGroupConfig
    {
        public PoolId poolId;
        public string resourcesFolder;
    }

    [SerializeField]
    private ResourceGroupConfig[] groups =
    {
        new ResourceGroupConfig { poolId = PoolId.Cloud, resourcesFolder = "Cloud" },
        new ResourceGroupConfig { poolId = PoolId.SafeMap, resourcesFolder = "SafeMap" },
        new ResourceGroupConfig { poolId = PoolId.NormalMap, resourcesFolder = "MapBase" },
        new ResourceGroupConfig { poolId = PoolId.TransitionWall, resourcesFolder = "TransitionWall" },
        new ResourceGroupConfig { poolId = PoolId.Missile, resourcesFolder = "Missile" },
        new ResourceGroupConfig { poolId = PoolId.MissileWarning, resourcesFolder = "MissileWarning" },
        new ResourceGroupConfig { poolId = PoolId.Effect, resourcesFolder = "Effect" },
        new ResourceGroupConfig { poolId = PoolId.MapStructure, resourcesFolder = "MapStructures" }
    };

    private readonly Dictionary<PoolId, string> groupPaths = new Dictionary<PoolId, string>();
    private readonly Dictionary<string, GameObject[]> prefabGroups = new Dictionary<string, GameObject[]>();
    private bool initialized;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        if (groups == null)
            return;

        for (int i = 0; i < groups.Length; i++)
        {
            ResourceGroupConfig group = groups[i];
            if (group == null)
                continue;

            string path = NormalizePath(group.resourcesFolder);
            if (path.Length == 0 || groupPaths.ContainsKey(group.poolId))
                continue;

            groupPaths.Add(group.poolId, path);
        }
    }

    public bool TryGetGroupPath(PoolId id, out string path)
    {
        Initialize();
        return groupPaths.TryGetValue(id, out path);
    }

    public GameObject[] LoadPrefabGroup(PoolId id)
    {
        if (!TryGetGroupPath(id, out string path))
            return Array.Empty<GameObject>();

        return LoadPrefabGroup(path);
    }

    public GameObject[] LoadPrefabGroup(string folder)
    {
        string path = NormalizePath(folder);
        if (path.Length == 0)
            return Array.Empty<GameObject>();

        if (prefabGroups.TryGetValue(path, out GameObject[] cached))
            return cached;

        GameObject[] prefabs = Resources.LoadAll<GameObject>(path);
        Array.Sort(prefabs, ComparePrefabNames);
        prefabGroups.Add(path, prefabs);
        return prefabs;
    }

    private static int ComparePrefabNames(GameObject first, GameObject second)
    {
        return StringComparer.Ordinal.Compare(first.name, second.name);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        string result = path.Trim().Replace('\\', '/').Trim('/');
        if (result.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase))
            result = result.Substring("Assets/Resources/".Length);
        else if (result.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            result = result.Substring("Resources/".Length);

        return result;
    }
}
