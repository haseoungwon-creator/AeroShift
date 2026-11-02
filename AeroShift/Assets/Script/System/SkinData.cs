using UnityEngine;

[CreateAssetMenu(menuName = "AeroFlip/Skin Data", fileName = "Skin Data")]
public class SkinData : ScriptableObject
{
    [SerializeField] string skinId;
    [SerializeField] string skinName;
    [SerializeField] Mesh skinMesh;
    [SerializeField] string requiredQuestId;
    [SerializeField] bool unlockedByDefault;

    public string SkinId => skinId;
    public string SkinName => skinName;
    public Mesh SkinMesh => skinMesh;
    public string RequiredQuestId => requiredQuestId;
    public bool UnlockedByDefault => unlockedByDefault;
}