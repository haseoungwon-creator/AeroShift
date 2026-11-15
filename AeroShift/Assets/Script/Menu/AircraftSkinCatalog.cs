using System;

// JsonUtility는 일반 C# 직렬화 클래스와 배열을 읽을 수 있습니다.
[Serializable]
public sealed class AircraftSkinCatalog
{
    public int version;
    public AircraftSkinDefinition[] aircraft;
}

[Serializable]
public sealed class AircraftSkinDefinition
{
    public string id;
    public string displayName;
    public string description;
    public bool unlockedByDefault;
    public string unlockAchievementId;
    public AircraftPassiveDefinition passive;
}

[Serializable]
public sealed class AircraftPassiveDefinition
{
    public string id;
    public string displayName;
    public string description;
    public float multiplier;
}
