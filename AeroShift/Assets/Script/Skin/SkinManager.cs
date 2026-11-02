using UnityEngine;

public class SkinManager : MonoBehaviour
{
    [SerializeField] SkinData[] skins;

    public int CurrentIndex { get; private set; }
    public int SelectedIndex { get; private set; }

    public SkinData CurrentSkin =>
        skins[CurrentIndex];

    public SkinData SelectedSkin =>
        skins[SelectedIndex];

    private void Awake()
    {
        CurrentIndex = 0;
        SelectedIndex = 0;
    }

    public void SelectNext()
    {
        if (skins == null || skins.Length == 0)
            return;

        SelectedIndex++;

        if (SelectedIndex >= skins.Length)
            SelectedIndex = 0;
    }

    public void SelectPrevious()
    {
        if (skins == null || skins.Length == 0)
            return;

        SelectedIndex--;

        if (SelectedIndex < 0)
            SelectedIndex = skins.Length - 1;
    }

    public bool IsUnlocked(int index)
    {
        if (index < 0 || index >= skins.Length)
            return false;

        SkinData skin = skins[index];

        if (skin.UnlockedByDefault)
            return true;

        if (string.IsNullOrEmpty(skin.RequiredQuestId))
            return false;

        return false;
    }

    public bool IsCurrentSkinUnlocked()
    {
        return IsUnlocked(SelectedIndex);
    }

    public void ConfirmSkin()
    {
        if (!IsCurrentSkinUnlocked())
            return;

        CurrentIndex = SelectedIndex;
    }

    public void ResetSelection()
    {
        SelectedIndex = CurrentIndex;
    }
}