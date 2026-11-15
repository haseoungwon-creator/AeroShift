using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AircraftPrefabBinding
{
    public string id;
    public GameObject prefab;
}

// 스킨 목록/선택/확정 저장을 전담한다. 실제 모델 생성은 MenuAircraftSpawner에서 처리한다.
public sealed class SkinManager : MonoBehaviour
{
    private const string SelectedAircraftKey = "AeroShift.SelectedAircraftId";

    [SerializeField] private TextAsset skinJson;
    [SerializeField] private AircraftPrefabBinding[] prefabBindings;

    // 기존 프로젝트에 연결한 SkinData 에셋이 있다면 그대로 사용할 수 있다
    [SerializeField] private SkinData[] skins;

    private readonly List<AircraftSkinDefinition> definitions = new List<AircraftSkinDefinition>();
    private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private readonly Dictionary<string, SkinData> skinAssets = new Dictionary<string, SkinData>(StringComparer.Ordinal);

    public int CurrentIndex { get; private set; } = -1;
    public int SelectedIndex { get; private set; } = -1;
    public int Count => definitions.Count;
    public bool HasSkins => definitions.Count > 0;

    public AircraftSkinDefinition CurrentInfo => GetInfo(CurrentIndex);
    public AircraftSkinDefinition SelectedInfo => GetInfo(SelectedIndex);

    // 이전 프로젝트가 SkinData를 사용하던 코드를 위한 호환 프로퍼티.
    public SkinData CurrentSkin => GetSkinAsset(CurrentIndex);
    public SkinData SelectedSkin => GetSkinAsset(SelectedIndex);

    public GameObject CurrentAircraftPrefab => GetPrefab(CurrentIndex);
    public GameObject SelectedAircraftPrefab => GetPrefab(SelectedIndex);

    public static string SavedAircraftId => PlayerPrefs.GetString(SelectedAircraftKey, string.Empty);

    private void Awake()
    {
        BuildPrefabLookups();
        ReadJson();

        CurrentIndex = FindFirstUnlockedIndex();
        string savedId = SavedAircraftId;
        for (int i = 0; i < definitions.Count; i++)
        {
            if (string.Equals(definitions[i].id, savedId, StringComparison.Ordinal) && IsUnlocked(i))
            {
                CurrentIndex = i;
                break;
            }
        }
        SelectedIndex = CurrentIndex;
    }

    private void BuildPrefabLookups()
    {
        if (skins != null)
        {
            foreach (SkinData skin in skins)
            {
                if (skin == null || string.IsNullOrWhiteSpace(skin.SkinId))
                    continue;

                if (skinAssets.ContainsKey(skin.SkinId))
                {
                    Debug.LogWarning("[SkinManager] SkinData ID 중복: " + skin.SkinId, this);
                    continue;
                }

                skinAssets.Add(skin.SkinId, skin);
                if (skin.AircraftPrefab != null)
                    prefabs.Add(skin.SkinId, skin.AircraftPrefab);
            }
        }

        // 새로운 Inspector 프리팹 목록이 기존 SkinData의 프리팹보다 우선한다.
        if (prefabBindings != null)
        {
            foreach (AircraftPrefabBinding binding in prefabBindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.id) || binding.prefab == null)
                    continue;

                prefabs[binding.id] = binding.prefab;
            }
        }
    }

    private void ReadJson()
    {
        if (skinJson == null)
        {
            Debug.LogError("[SkinManager] Skin Json(TextAsset)을 Inspector에 연결하세요.", this);
            return;
        }

        AircraftSkinCatalog catalog;
        try
        {
            catalog = JsonUtility.FromJson<AircraftSkinCatalog>(skinJson.text);
        }
        catch (Exception ex)
        {
            Debug.LogError("[SkinManager] JSON 파싱 실패: " + ex.Message, this);
            return;
        }

        if (catalog == null || catalog.aircraft == null || catalog.aircraft.Length == 0)
        {
            Debug.LogError("[SkinManager] JSON에 aircraft 배열이 없습니다.", this);
            return;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AircraftSkinDefinition item in catalog.aircraft)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id))
            {
                Debug.LogWarning("[SkinManager] ID가 비어 있는 스킨은 무시합니다.", this);
                continue;
            }
            if (!ids.Add(item.id))
            {
                Debug.LogWarning("[SkinManager] JSON ID 중복: " + item.id, this);
                continue;
            }
            definitions.Add(item);
            if (!prefabs.ContainsKey(item.id))
                Debug.LogWarning("[SkinManager] 프리팹 미연결: " + item.id, this);
        }
    }

    public void SelectNext()
    {
        if (!HasSkins) return;
        SelectedIndex = (SelectedIndex + 1) % definitions.Count;
    }

    public void SelectPrevious()
    {
        if (!HasSkins) return;
        SelectedIndex = (SelectedIndex - 1 + definitions.Count) % definitions.Count;
    }

    public bool IsUnlocked(int index)
    {
        AircraftSkinDefinition info = GetInfo(index);
        // 현재 JSON의 모든 항목은 true. 이후 업적 시스템에서 여기만 확장한다.
        return info != null && info.unlockedByDefault;
    }

    public bool IsCurrentSkinUnlocked() => IsUnlocked(SelectedIndex);

    public bool CanConfirmSelected => IsUnlocked(SelectedIndex) && SelectedAircraftPrefab != null;

    public bool TryConfirmSkin()
    {
        if (!CanConfirmSelected)
        {
            Debug.LogWarning("[SkinManager] 선택할 수 없는 비행기이거나 프리팹이 없습니다.", this);
            return false;
        }

        CurrentIndex = SelectedIndex;
        PlayerPrefs.SetString(SelectedAircraftKey, definitions[CurrentIndex].id);
        PlayerPrefs.Save();
        return true;
    }

    // 기존 MenuAircraftSpawner 등의 호출과 호환되는 메서드.
    public void ConfirmSkin() => TryConfirmSkin();

    public void ResetSelection() => SelectedIndex = CurrentIndex;

    private int FindFirstUnlockedIndex()
    {
        for (int i = 0; i < definitions.Count; i++)
            if (IsUnlocked(i)) return i;
        return -1;
    }

    private AircraftSkinDefinition GetInfo(int index)
    {
        return index >= 0 && index < definitions.Count ? definitions[index] : null;
    }

    private SkinData GetSkinAsset(int index)
    {
        AircraftSkinDefinition info = GetInfo(index);
        if (info == null) return null;
        skinAssets.TryGetValue(info.id, out SkinData asset);
        return asset;
    }

    private GameObject GetPrefab(int index)
    {
        AircraftSkinDefinition info = GetInfo(index);
        if (info == null) return null;
        prefabs.TryGetValue(info.id, out GameObject prefab);
        return prefab;
    }
}
