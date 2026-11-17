using UnityEngine;

public class SkillSelectionManager : MonoBehaviour
{
    [SerializeField] private SkillSelectionUI[] skillSelections;

    private SkillJsonData skillData;

    public SkillInfo SelectedSkill { get; private set; }
    public string SelectedSkillId { get; private set; }

    private void Awake()
    {
        LoadSkills();
    }

    private void OnEnable()
    {
        ShowRandomSkills();
    }

    private void LoadSkills()
    {
        TextAsset json = Resources.Load<TextAsset>("Config/Skills");

        if (json == null)
            return;

        skillData = JsonUtility.FromJson<SkillJsonData>(json.text);
    }

    public void ShowRandomSkills()
    {
        if (skillData == null || skillData.skills == null)
            return;

        if (skillSelections == null || skillSelections.Length != 3)
            return;

        if (skillData.skills.Length < 3)
            return;

        int[] indexes = new int[skillData.skills.Length];

        for (int i = 0; i < indexes.Length; i++)
            indexes[i] = i;

        for (int i = 0; i < skillSelections.Length; i++)
        {
            int randomIndex = Random.Range(i, indexes.Length);

            int temp = indexes[i];
            indexes[i] = indexes[randomIndex];
            indexes[randomIndex] = temp;

            if (skillSelections[i] != null)
                skillSelections[i].SetSkill(skillData.skills[indexes[i]], this);
        }

        SelectedSkill = null;
        SelectedSkillId = "";
    }

    public void SelectSkill(SkillInfo info)
    {
        if (info == null)
            return;

        SelectedSkill = info;
        SelectedSkillId = info.id;
    }
}
