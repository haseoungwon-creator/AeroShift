#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Repairs missing scene-side pool/resource configuration for JSON-based maps.
/// Add this file under Assets/Editor. Run the menu before entering Play Mode.
/// It does not replace existing pool parents or clear any already-configured groups.
/// </summary>
internal static class AeroShiftJsonMapSetupRepairEditor
{
    private const string SettingsFolder = "Assets/Resources/Config/Settings/";
    private const string LayoutPath = "Assets/Resources/Config/Maps/MapLayouts.json";

    private struct GroupSpec
    {
        public PoolId Id;
        public string Folder;
        public int InitialCount;
        public string ParentName;
        public GroupSpec(PoolId id, string folder, int initialCount, string parentName)
        {
            Id = id;
            Folder = folder;
            InitialCount = initialCount;
            ParentName = parentName;
        }
    }

    private static readonly GroupSpec[] Required =
    {
        new GroupSpec(PoolId.SafeMap, "SafeMap", 3, "SafeMapPool"),
        new GroupSpec(PoolId.NormalMap, "MapBase", 14, "MapBasePool"),
        new GroupSpec(PoolId.MapStructure, "MapStructures", 100, "MapStructurePool")
    };

    [MenuItem("AeroShift/Maps/Repair JSON Map Pool Setup (Open Scenes)")]
    private static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("AeroShift Map Setup", "Exit Play Mode before repairing scene references.", "OK");
            return;
        }

        int poolGroupsAdded = 0;
        int resourceGroupsUpdated = 0;
        int settingsLinked = 0;
        int scenesChanged = 0;
        int sceneCount = 0;
        var messages = new List<string>();

        for (int i = 0; i < SceneManager.sceneCount; ++i)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            sceneCount++;
            bool dirty = false;
            List<PoolManager> pools = FindSceneComponents<PoolManager>(scene);
            List<ResourceManager> resources = FindSceneComponents<ResourceManager>(scene);
            List<MapManager> maps = FindSceneComponents<MapManager>(scene);

            if (pools.Count == 0 || resources.Count == 0)
            {
                messages.Add(scene.name + ": PoolManager or ResourceManager component is missing.");
                continue;
            }

            foreach (ResourceManager resource in resources)
            {
                SerializedObject so = new SerializedObject(resource);
                SerializedProperty groups = so.FindProperty("groups");
                if (groups == null || !groups.isArray)
                {
                    messages.Add(scene.name + ": ResourceManager.groups could not be found.");
                    continue;
                }

                so.Update();
                bool edited = false;
                foreach (GroupSpec spec in Required)
                {
                    SerializedProperty group = FindGroup(groups, "poolId", spec.Id);
                    if (group == null)
                    {
                        groups.InsertArrayElementAtIndex(groups.arraySize);
                        group = groups.GetArrayElementAtIndex(groups.arraySize - 1);
                        group.FindPropertyRelative("poolId").enumValueIndex = (int)spec.Id;
                        group.FindPropertyRelative("resourcesFolder").stringValue = spec.Folder;
                        resourceGroupsUpdated++;
                        edited = true;
                    }
                    else
                    {
                        SerializedProperty folder = group.FindPropertyRelative("resourcesFolder");
                        if (folder != null && folder.stringValue != spec.Folder)
                        {
                            folder.stringValue = spec.Folder;
                            resourceGroupsUpdated++;
                            edited = true;
                        }
                    }
                }
                if (edited)
                {
                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(resource);
                    dirty = true;
                }
            }

            foreach (PoolManager pool in pools)
            {
                SerializedObject so = new SerializedObject(pool);
                SerializedProperty groups = so.FindProperty("groups");
                if (groups == null || !groups.isArray)
                {
                    messages.Add(scene.name + ": PoolManager.groups could not be found.");
                    continue;
                }

                so.Update();
                bool edited = false;
                foreach (GroupSpec spec in Required)
                {
                    // Existing groups are deliberately preserved, including their parent references.
                    if (FindGroup(groups, "id", spec.Id) != null)
                        continue;

                    Transform parent = FindOrCreateParent(pool.transform, spec.ParentName);
                    groups.InsertArrayElementAtIndex(groups.arraySize);
                    SerializedProperty group = groups.GetArrayElementAtIndex(groups.arraySize - 1);
                    group.FindPropertyRelative("id").enumValueIndex = (int)spec.Id;
                    group.FindPropertyRelative("parent").objectReferenceValue = parent;
                    group.FindPropertyRelative("resourceInitialCount").intValue = spec.InitialCount;
                    group.FindPropertyRelative("resourceExpandWhenEmpty").boolValue = true;
                    group.FindPropertyRelative("initializeOnFirstUse").boolValue = false;
                    group.FindPropertyRelative("enableAutoShrink").boolValue = false;
                    group.FindPropertyRelative("idleSecondsBeforeShrink").floatValue = 60f;
                    group.FindPropertyRelative("shrinkCheckIntervalSeconds").floatValue = 5f;
                    poolGroupsAdded++;
                    edited = true;
                }

                if (TryConnectSettings(so, "PoolManager.json"))
                {
                    settingsLinked++;
                    edited = true;
                }
                if (edited)
                {
                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pool);
                    dirty = true;
                }
            }

            foreach (MapManager manager in maps)
            {
                SerializedObject so = new SerializedObject(manager);
                so.Update();
                if (TryConnectSettings(so, "MapManager.json"))
                {
                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                    settingsLinked++;
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                scenesChanged++;
            }
        }

        ValidateAssets(messages);
        foreach (string message in messages)
            Debug.LogWarning("[AeroShift Map Setup] " + message);

        string result = "Scenes checked: " + sceneCount +
                        "\nScenes modified: " + scenesChanged +
                        "\nPool groups added: " + poolGroupsAdded +
                        "\nResource mappings added/fixed: " + resourceGroupsUpdated +
                        "\nJSON settings references connected: " + settingsLinked +
                        "\nAsset warnings: " + messages.Count +
                        "\n\nSave the scene (Ctrl+S), then enter Play Mode.";
        Debug.Log("[AeroShift Map Setup] " + result.Replace('\n', ' '));
        EditorUtility.DisplayDialog("AeroShift JSON Map Setup", result, "OK");
    }

    [MenuItem("AeroShift/Maps/Check JSON Map Assets")]
    private static void Check()
    {
        var warnings = new List<string>();
        ValidateAssets(warnings);
        foreach (string warning in warnings)
            Debug.LogWarning("[AeroShift Map Setup] " + warning);
        EditorUtility.DisplayDialog("JSON Map Asset Check",
            warnings.Count == 0
                ? "MapBase, SafeMap, MapStructures, and layout JSON passed asset checks.\n\nThis does not validate scene pool groups; run Repair in the Main scene."
                : warnings.Count + " warning(s). See the Console for details.", "OK");
    }

    private static bool TryConnectSettings(SerializedObject so, string filename)
    {
        SerializedProperty settings = so.FindProperty("settingsJson");
        if (settings == null || settings.propertyType != SerializedPropertyType.ObjectReference ||
            settings.objectReferenceValue != null)
            return false;

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(SettingsFolder + filename);
        if (json == null)
            return false;

        settings.objectReferenceValue = json;
        return true;
    }

    private static SerializedProperty FindGroup(SerializedProperty array, string idName, PoolId id)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            SerializedProperty idField = element.FindPropertyRelative(idName);
            if (idField != null && idField.enumValueIndex == (int)id)
                return element;
        }
        return null;
    }

    private static Transform FindOrCreateParent(Transform root, string name)
    {
        Transform existing = root.Find(name);
        if (existing != null)
            return existing;

        GameObject child = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(child, "Create AeroShift pool parent");
        Undo.SetTransformParent(child.transform, root, "Parent AeroShift pool root");
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child.transform;
    }

    private static List<T> FindSceneComponents<T>(Scene scene) where T : Component
    {
        var result = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<T>(true));
        return result;
    }

    private static int CountPrefabs(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return 0;
        return AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Length;
    }

    private static void ValidateAssets(List<string> warnings)
    {
        if (CountPrefabs("Assets/Resources/MapBase") != 1)
            warnings.Add("Resources/MapBase must contain exactly one base prefab (roadBase.prefab).");
        if (CountPrefabs("Assets/Resources/SafeMap") == 0)
            warnings.Add("Resources/SafeMap contains no prefabs.");
        if (CountPrefabs("Assets/Resources/MapStructures") == 0)
            warnings.Add("Resources/MapStructures contains no structure prefabs.");

        TextAsset layouts = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
        if (layouts == null)
        {
            warnings.Add("Missing JSON: " + LayoutPath);
            return;
        }

        MapLayoutDatabase database;
        try { database = JsonUtility.FromJson<MapLayoutDatabase>(layouts.text); }
        catch (Exception e)
        {
            warnings.Add("MapLayouts.json cannot be parsed: " + e.Message);
            return;
        }
        if (database == null || database.schemaVersion != 1 ||
            database.maps == null || database.maps.Length == 0)
        {
            warnings.Add("MapLayouts.json has no valid version-1 maps array.");
            return;
        }

        var prefabs = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/MapStructures" }))
            prefabs.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));

        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MapLayoutDefinition map in database.maps)
        {
            if (map == null || string.IsNullOrEmpty(map.id) || !mapIds.Add(map.id) || map.structures == null)
            {
                warnings.Add("MapLayouts.json contains an invalid or duplicate map ID or structures list.");
                continue;
            }
            foreach (MapStructurePlacement placement in map.structures)
            {
                if (placement == null || !prefabs.Contains(placement.prefabId))
                    warnings.Add(map.id + ": prefabId '" + (placement == null ? "<null>" : placement.prefabId) + "' has no prefab in Resources/MapStructures.");
            }
        }
    }
}
#endif
