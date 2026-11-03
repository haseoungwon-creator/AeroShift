using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-600)]
[DisallowMultipleComponent]
public sealed class ResourceManager : MonoBehaviour
{
    [Serializable]
    public sealed class ResourceGroupConfig
    {
        public PoolId poolId;
        public string resourcesFolder;
    }

    [Header("Resource Groups")]
    [SerializeField]
    private ResourceGroupConfig[] groups =
    {
        new ResourceGroupConfig { poolId = PoolId.Cloud, resourcesFolder = "Cloud" },
        new ResourceGroupConfig { poolId = PoolId.SafeMap, resourcesFolder = "SafeMap" },
        new ResourceGroupConfig { poolId = PoolId.NormalMap, resourcesFolder = "NormalMap" },
        new ResourceGroupConfig { poolId = PoolId.TransitionWall, resourcesFolder = "TransitionWall" },
        new ResourceGroupConfig { poolId = PoolId.Missile, resourcesFolder = "Missile" },
        new ResourceGroupConfig { poolId = PoolId.MissileWarning, resourcesFolder = "MissileWarning" },
        new ResourceGroupConfig { poolId = PoolId.Effect, resourcesFolder = "Effect" }
    };

    private readonly Dictionary<PoolId, string> groupPaths =
        new Dictionary<PoolId, string>();

    private readonly Dictionary<string, GameObject[]> groupCache =
        new Dictionary<string, GameObject[]>(StringComparer.Ordinal);

    private readonly Dictionary<string, GameObject> prefabCache =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private readonly Dictionary<string, List<Action<GameObject>>> pendingLoads =
        new Dictionary<string, List<Action<GameObject>>>(StringComparer.Ordinal);

    private bool groupPathsInitialized;
    private bool isShuttingDown;

    public int RegisteredGroupCount
    {
        get
        {
            InitializeGroupPaths();
            return groupPaths.Count;
        }
    }

    public int CachedGroupCount => groupCache.Count;
    public int CachedPrefabCount => prefabCache.Count;
    public int PendingLoadCount => pendingLoads.Count;

    private void Awake()
    {
        InitializeGroupPaths();
    }

    private void InitializeGroupPaths()
    {
        if (groupPathsInitialized)
            return;

        groupPathsInitialized = true;
        groupPaths.Clear();

        if (groups == null)
            return;

        for (int i = 0; i < groups.Length; i++)
        {
            ResourceGroupConfig group = groups[i];

            if (group == null)
                continue;

            string path = NormalizePath(group.resourcesFolder);

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"{group.poolId}의 Resources 폴더 경로가 비어 있습니다.", this);
                continue;
            }

            if (groupPaths.ContainsKey(group.poolId))
            {
                Debug.LogError($"중복 PoolId 설정: {group.poolId}", this);
                continue;
            }

            groupPaths.Add(group.poolId, path);
        }
    }

    public bool TryGetGroupPath(PoolId id, out string resourceFolder)
    {
        InitializeGroupPaths();
        return groupPaths.TryGetValue(id, out resourceFolder);
    }

    public GameObject[] LoadPrefabGroup(PoolId id)
    {
        if (!TryGetGroupPath(id, out string folder))
        {
            Debug.LogError($"등록되지 않은 리소스 그룹: {id}", this);
            return Array.Empty<GameObject>();
        }

        return LoadPrefabGroup(folder);
    }

    public GameObject[] LoadPrefabGroup(string resourceFolder)
    {
        string path = NormalizePath(resourceFolder);

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("Resources 폴더 경로가 비어 있습니다.", this);
            return Array.Empty<GameObject>();
        }

        if (groupCache.TryGetValue(path, out GameObject[] cached))
            return cached;

        GameObject[] prefabs = Resources.LoadAll<GameObject>(path);

        Array.Sort(prefabs, (a, b) =>
            StringComparer.Ordinal.Compare(a.name, b.name));

        if (prefabs.Length == 0)
            Debug.LogWarning($"Resources/{path}에 프리팹이 없습니다.", this);

        groupCache.Add(path, prefabs);
        return prefabs;
    }

    public GameObject LoadPrefab(PoolId id, string prefabName)
    {
        if (!TryGetGroupPath(id, out string folder))
        {
            Debug.LogError($"등록되지 않은 리소스 그룹: {id}", this);
            return null;
        }

        return LoadPrefab(CombinePath(folder, prefabName));
    }

    public GameObject LoadPrefab(string resourcePath)
    {
        string path = NormalizePath(resourcePath);

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("프리팹 경로가 비어 있습니다.", this);
            return null;
        }

        if (prefabCache.TryGetValue(path, out GameObject cached) && cached != null)
            return cached;

        GameObject prefab = Resources.Load<GameObject>(path);

        if (prefab == null)
        {
            Debug.LogError($"Resources/{path} 프리팹을 찾지 못했습니다.", this);
            return null;
        }

        prefabCache[path] = prefab;
        return prefab;
    }

    public void LoadPrefabAsync(PoolId id, string prefabName, Action<GameObject> onCompleted)
    {
        if (!TryGetGroupPath(id, out string folder))
        {
            Debug.LogError($"등록되지 않은 리소스 그룹: {id}", this);
            InvokeSafely(onCompleted, null);
            return;
        }

        LoadPrefabAsync(CombinePath(folder, prefabName), onCompleted);
    }

    public void LoadPrefabAsync(string resourcePath, Action<GameObject> onCompleted)
    {
        if (isShuttingDown)
        {
            InvokeSafely(onCompleted, null);
            return;
        }

        string path = NormalizePath(resourcePath);

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("비동기 로딩 경로가 비어 있습니다.", this);
            InvokeSafely(onCompleted, null);
            return;
        }

        if (prefabCache.TryGetValue(path, out GameObject cached) && cached != null)
        {
            InvokeSafely(onCompleted, cached);
            return;
        }

        if (pendingLoads.TryGetValue(path, out List<Action<GameObject>> waiting))
        {
            if (onCompleted != null)
                waiting.Add(onCompleted);

            return;
        }

        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("비활성 ResourceManager에서는 비동기 로딩을 시작할 수 없습니다.", this);
            InvokeSafely(onCompleted, null);
            return;
        }

        waiting = new List<Action<GameObject>>();

        if (onCompleted != null)
            waiting.Add(onCompleted);

        pendingLoads.Add(path, waiting);
        StartCoroutine(LoadPrefabRoutine(path));
    }

    private IEnumerator LoadPrefabRoutine(string path)
    {
        ResourceRequest request = Resources.LoadAsync<GameObject>(path);
        yield return request;

        GameObject prefab = request.asset as GameObject;

        if (prefab != null)
            prefabCache[path] = prefab;
        else
            Debug.LogError($"Resources/{path} 비동기 로딩에 실패했습니다.", this);

        if (!pendingLoads.TryGetValue(path, out List<Action<GameObject>> callbacks))
            yield break;

        pendingLoads.Remove(path);

        for (int i = 0; i < callbacks.Count; i++)
            InvokeSafely(callbacks[i], prefab);
    }

    public void ClearCachedReferences()
    {
        groupCache.Clear();
        prefabCache.Clear();
    }

    [ContextMenu("Validate Configured Resource Groups")]
    private void ValidateConfiguredGroups()
    {
        InitializeGroupPaths();

        foreach (KeyValuePair<PoolId, string> pair in groupPaths)
        {
            GameObject[] prefabs = LoadPrefabGroup(pair.Value);
            Debug.Log($"{pair.Key} → Resources/{pair.Value}: {prefabs.Length}개 프리팹", this);
        }
    }

    private void OnDestroy()
    {
        isShuttingDown = true;
        List<Action<GameObject>> callbacks = new List<Action<GameObject>>();

        foreach (List<Action<GameObject>> waiting in pendingLoads.Values)
            callbacks.AddRange(waiting);

        pendingLoads.Clear();

        for (int i = 0; i < callbacks.Count; i++)
            InvokeSafely(callbacks[i], null);
    }

    private void InvokeSafely(Action<GameObject> callback, GameObject prefab)
    {
        if (callback == null)
            return;

        try
        {
            callback(prefab);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private static string CombinePath(string folder, string prefabName)
    {
        string name = NormalizePath(prefabName);

        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return NormalizePath(folder) + "/" + name;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string result = path.Trim().Replace('\\', '/').Trim('/');

        const string assetsPrefix = "Assets/Resources/";
        const string resourcesPrefix = "Resources/";

        if (result.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            result = result.Substring(assetsPrefix.Length);
        else if (result.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            result = result.Substring(resourcesPrefix.Length);

        if (result.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            result = result.Substring(0, result.Length - ".prefab".Length);

        return result.Trim('/');
    }
}
