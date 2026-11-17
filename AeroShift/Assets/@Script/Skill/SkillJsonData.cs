using System;

[Serializable]
public class SkillInfo
{
    public string id;
    public string name;
    public string description;
    public string iconID;

    public float skillValue;
    public bool canReroll;
}

[Serializable]
public class SkillJsonData
{
    public SkillInfo[] skills;
}
