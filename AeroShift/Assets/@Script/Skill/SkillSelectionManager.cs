using UnityEngine;

public class SkillSelectionManager : MonoBehaviour
{
    [SerializeField] private SkillSelectionUI[] skillSelections;
    [SerializeField] private SelectedSkillSO selectedSkillSO;
    [SerializeField] private GameObject skillSelectPanel;
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private GameManager gameManager;

    private SkillDataManager skillDataManager;
    private int[] remainingRerolls;
    private int bonusRerolls;

    public SkillInfo SelectedSkill { get; private set; }
    public string SelectedSkillId { get; private set; }

    private void Awake()
    {
        skillDataManager = new SkillDataManager();
        skillDataManager.LoadSkills();
    }

    private void OnEnable()
    {
        ShowRandomSkills();
    }

    public void ShowRandomSkills()
    {
        if(skillSelectPanel != null)
            skillSelectPanel.SetActive(true);

        if (selectedSkillSO != null)
            selectedSkillSO.ClearSkill();

        if (playerStats != null)
            playerStats.LoadStats();

        if (skillDataManager == null || skillSelections == null || skillSelections.Length != 3)
            return;

        SkillInfo[] skills = skillDataManager.GetRandomSkills(3);

        if (skills == null)
            return;

        remainingRerolls = new int[skillSelections.Length];

        for (int i = 0; i < skillSelections.Length; i++)
        {
            remainingRerolls[i] = 1 + bonusRerolls;

            if (skillSelections[i] == null)
                continue;

            skillSelections[i].SetSkill(skills[i], this);
            UpdateRerollButton(i);
        }

        SelectedSkill = null;
        SelectedSkillId = "";
    }

    public void RerollSkill(SkillSelectionUI selection)
    {
        if (selection == null || remainingRerolls == null)
            return;

        int index = -1;

        for (int i = 0; i < skillSelections.Length; i++)
        {
            if (skillSelections[i] == selection)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || remainingRerolls[index] <= 0)
            return;

        SkillInfo currentSkill = selection.CurrentSkill;

        if (currentSkill == null || !currentSkill.canReroll)
            return;

        string firstId = currentSkill.id;
        string secondId = "";
        string thirdId = "";

        for (int i = 0; i < skillSelections.Length; i++)
        {
            if (i == index || skillSelections[i] == null)
                continue;

            SkillInfo otherSkill = skillSelections[i].CurrentSkill;

            if (otherSkill == null)
                continue;

            if (secondId == "")
                secondId = otherSkill.id;
            else
                thirdId = otherSkill.id;
        }

        SkillInfo newSkill = skillDataManager.GetRandomSkillExcept(firstId, secondId, thirdId);

        if (newSkill == null)
            return;

        if (SelectedSkill == currentSkill)
        {
            SelectedSkill = null;
            SelectedSkillId = "";
        }

        remainingRerolls[index]--;
        selection.SetSkill(newSkill, this);
        UpdateRerollButton(index);
    }

    public void SelectSkill(SkillInfo info)
    {
        if (info == null || selectedSkillSO == null)
            return;

        selectedSkillSO.SetSkill(info);

        if (playerStats != null)
            playerStats.LoadStats();

        SelectedSkill = info;
        SelectedSkillId = info.id;

        if (skillSelectPanel != null)
            skillSelectPanel.SetActive(false);

        if (gameManager != null)
            gameManager.StartGame();
    }

    public void SetBonusRerolls(int count)
    {
        if (count < 0)
            count = 0;

        int difference = count - bonusRerolls;
        bonusRerolls = count;

        if (remainingRerolls == null)
            return;

        for (int i = 0; i < remainingRerolls.Length; i++)
        {
            remainingRerolls[i] += difference;

            if (remainingRerolls[i] < 0)
                remainingRerolls[i] = 0;

            UpdateRerollButton(i);
        }
    }

    private void UpdateRerollButton(int index)
    {
        if (skillSelections[index] == null)
            return;

        SkillInfo info = skillSelections[index].CurrentSkill;
        bool available = info != null && info.canReroll && remainingRerolls[index] > 0;

        skillSelections[index].SetRerollAvailable(available);
    }
}
