using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public abstract class JsonSettingsData
{
    public int schemaVersion = 1;
    public string configType;

    public void Validate()
    {
        JsonSettingRules.Require(schemaVersion == 1, "unsupported schemaVersion");
        JsonSettingRules.Require(configType == GetType().Name, "configType does not match the component");
        ValidateValues();
    }

    protected abstract void ValidateValues();
}

public static class JsonSettingRules
{
    public static void Require(bool condition, string field)
    {
        if (!condition)
            throw new ArgumentException("Invalid JSON setting: " + field);
    }

    public static void Finite(float value, string field)
    {
        Require(!float.IsNaN(value) && !float.IsInfinity(value), field);
    }

    public static void Ordered(float minimum, float maximum, string field)
    {
        Require(minimum <= maximum, field);
    }
}

public static class JsonSettingsStore
{
    private static readonly Dictionary<(TextAsset, Type), JsonSettingsData> cache =
        new Dictionary<(TextAsset, Type), JsonSettingsData>();
    private static readonly Dictionary<string, TextAsset> resourceAssets =
        new Dictionary<string, TextAsset>();
    private static readonly Dictionary<Type, JsonSettingsData> defaults =
        new Dictionary<Type, JsonSettingsData>();
    private static readonly HashSet<string> reportedErrors = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearCache()
    {
        cache.Clear();
        resourceAssets.Clear();
        defaults.Clear();
        reportedErrors.Clear();
    }

    public static T Parse<T>(string json) where T : JsonSettingsData, new()
    {
        JsonSettingRules.Require(!string.IsNullOrWhiteSpace(json), "empty document");
        string trimmed = json.Trim();
        JsonSettingRules.Require(trimmed.StartsWith("{") && trimmed.EndsWith("}"), "root must be an object");
        T data = new T();
        JsonUtility.FromJsonOverwrite(json, data);
        data.Validate();
        return data;
    }

    public static T Load<T>(TextAsset assignedAsset, string resourcePath) where T : JsonSettingsData, new()
    {
        TextAsset asset = assignedAsset;
        if (asset == null && !resourceAssets.TryGetValue(resourcePath, out asset))
        {
            asset = Resources.Load<TextAsset>(resourcePath);
            resourceAssets.Add(resourcePath, asset);
        }

        if (asset == null)
        {
            Report(resourcePath, "JSON file is missing. Using recovery defaults.", null);
            return GetDefaults<T>();
        }

        var key = (asset, typeof(T));
        if (cache.TryGetValue(key, out JsonSettingsData cached))
            return (T)cached;

        T result;
        try
        {
            result = Parse<T>(asset.text);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
        {
            Report(asset.name + typeof(T).Name,
                exception.Message + " Using recovery defaults for " + typeof(T).Name + ".", asset);
            result = GetDefaults<T>();
        }

        cache.Add(key, result);
        return result;
    }

    private static T GetDefaults<T>() where T : JsonSettingsData, new()
    {
        if (defaults.TryGetValue(typeof(T), out JsonSettingsData data))
            return (T)data;
        T created = new T();
        created.Validate();
        defaults.Add(typeof(T), created);
        return created;
    }

    private static void Report(string key, string message, UnityEngine.Object context)
    {
        if (reportedErrors.Add(key))
            Debug.LogError("[AeroShift JSON] " + key + ": " + message, context);
    }
}
