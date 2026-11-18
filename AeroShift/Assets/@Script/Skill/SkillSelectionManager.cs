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

    public SkillInfo SelectedSkill
    {
        get { return selectedSkillSO != null ? selectedSkillSO.CurrentSkill : null; }
    }

    public string SelectedSkillId
    {
        get { return SelectedSkill != null ? SelectedSkill.id : ""; }
    }

    private void Awake()
    {
        skillDataManager = new SkillDataManager();
        skillDataManager.LoadSkills();
    }

    public void ShowRandomSkills()
    {
        if (skillSelectPanel != null)
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

        if (remainingRerolls == null || remainingRerolls.Length != skillSelections.Length)
            remainingRerolls = new int[skillSelections.Length];

        for (int i = 0; i < skillSelections.Length; i++)
        {
            remainingRerolls[i] = 1 + bonusRerolls;

            if (skillSelections[i] == null)
                continue;

            skillSelections[i].SetSkill(skills[i], this);
            UpdateRerollButton(i);
        }

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
