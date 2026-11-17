using UnityEngine;

public class SkillDataManager
{
    private SkillJsonData skillData;

    public void LoadSkills()
    {
        TextAsset json = Resources.Load<TextAsset>("Config/AircraftSkill");

        if (json == null)
            return;

        skillData = JsonUtility.FromJson<SkillJsonData>(json.text);
    }

    public SkillInfo[] GetRandomSkills(int count)
    {
        if (skillData == null)
            LoadSkills();

        if (skillData == null || skillData.skills == null)
            return null;

        if (count <= 0 || skillData.skills.Length < count)
            return null;

        int[] indexes = new int[skillData.skills.Length];

        for (int i = 0; i < indexes.Length; i++)
            indexes[i] = i;

        SkillInfo[] selectedSkills = new SkillInfo[count];

        for (int i = 0; i < count; i++)
        {
            int randomIndex = Random.Range(i, indexes.Length);

            int temp = indexes[i];
            indexes[i] = indexes[randomIndex];
            indexes[randomIndex] = temp;

            selectedSkills[i] = skillData.skills[indexes[i]];
        }

        return selectedSkills;
    }

    public SkillInfo GetRandomSkillExcept(string firstId, string secondId, string thirdId)
    {
        if (skillData == null)
            LoadSkills();

        if (skillData == null || skillData.skills == null)
            return null;

        int availableCount = 0;

        for (int i = 0; i < skillData.skills.Length; i++)
        {
            if (IsAvailable(skillData.skills[i], firstId, secondId, thirdId))
                availableCount++;
        }

        if (availableCount == 0)
            return null;

        int randomIndex = Random.Range(0, availableCount);

        for (int i = 0; i < skillData.skills.Length; i++)
        {
            SkillInfo info = skillData.skills[i];

            if (!IsAvailable(info, firstId, secondId, thirdId))
                continue;

            if (randomIndex == 0)
                return info;

            randomIndex--;
        }

        return null;
    }

    private bool IsAvailable(SkillInfo info, string firstId, string secondId, string thirdId)
    {
        if (info == null || string.IsNullOrEmpty(info.id))
            return false;

        if (info.id == firstId || info.id == secondId || info.id == thirdId)
            return false;

        return true;
    }
}
