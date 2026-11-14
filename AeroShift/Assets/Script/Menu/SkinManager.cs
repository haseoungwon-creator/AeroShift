using System;
using UnityEngine;

public class SkinManager : MonoBehaviour
{
    private const string SelectedAircraftKey = "AeroShift.SelectedAircraftId";

    [SerializeField] private SkinData[] skins;

    public int CurrentIndex { get; private set; }
    public int SelectedIndex { get; private set; }

    public SkinData CurrentSkin => GetSkin(CurrentIndex);
    public SkinData SelectedSkin => GetSkin(SelectedIndex);

    private void Awake()
    {
        CurrentIndex = FindFirstUnlockedIndex();

        string savedId = PlayerPrefs.GetString(SelectedAircraftKey, string.Empty);
        if (!string.IsNullOrEmpty(savedId) && skins != null)
        {
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i] != null &&
                    string.Equals(skins[i].SkinId, savedId, StringComparison.Ordinal) &&
                    IsUnlocked(i))
                {
                    CurrentIndex = i;
                    break;
                }
            }
        }

        SelectedIndex = CurrentIndex;
    }

    public void SelectNext()
    {
        if (skins == null || skins.Length == 0)
            return;

        SelectedIndex = (SelectedIndex + 1) % skins.Length;
    }

    public void SelectPrevious()
    {
        if (skins == null || skins.Length == 0)
            return;

        SelectedIndex = (SelectedIndex - 1 + skins.Length) % skins.Length;
    }

    public bool IsUnlocked(int index)
    {
        SkinData skin = GetSkin(index);
        if (skin == null)
            return false;

        if (skin.UnlockedByDefault)
            return true;

        // 업적 해금 시스템은 후속 단계에서 연결한다.
        // 현재는 기본 해금 비행기만 확정할 수 있다.
        return false;
    }

    // 기존 코드에서 사용하는 메서드명 유지.
    public bool IsCurrentSkinUnlocked() => IsUnlocked(SelectedIndex);

    public void ConfirmSkin()
    {
        if (!IsUnlocked(SelectedIndex))
            return;

        SkinData data = SelectedSkin;
        if (data == null || string.IsNullOrWhiteSpace(data.SkinId))
            return;

        CurrentIndex = SelectedIndex;
        PlayerPrefs.SetString(SelectedAircraftKey, data.SkinId);
        PlayerPrefs.Save();
    }

    public void ResetSelection()
    {
        SelectedIndex = CurrentIndex;
    }

    private SkinData GetSkin(int index)
    {
        if (skins == null || index < 0 || index >= skins.Length)
            return null;

        return skins[index];
    }

    private int FindFirstUnlockedIndex()
    {
        if (skins == null)
            return 0;

        for (int i = 0; i < skins.Length; i++)
            if (IsUnlocked(i))
                return i;

        return 0;
    }
}
