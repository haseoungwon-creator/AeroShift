using UnityEngine;

public class SkillDataManager : MonoBehaviour
{
    private SkillJsonData skillData;

    private void Awake()
    {
        LoadSkills();
    }

    private void LoadSkills()
    {
        TextAsset json = Resources.Load<TextAsset>("Config/Skills");

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
}
