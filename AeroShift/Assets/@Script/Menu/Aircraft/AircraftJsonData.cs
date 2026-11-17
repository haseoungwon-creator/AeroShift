using System;

[Serializable]
public class AircraftJsonData
{
    public int version;
    public AircraftInfo[] aircraft;
}

[Serializable]
public class AircraftInfo
{
    public string id;
    public string displayName;
    public string description;
    public bool unlockedByDefault;
    public string unlockAchievementId;
    public AircraftPassiveInfo passive;
}

[Serializable]
public class AircraftPassiveInfo
{
    public string id;
    public string displayName;
    public string description;
    public float multiplier;
    public int bonusRerolls;
}
