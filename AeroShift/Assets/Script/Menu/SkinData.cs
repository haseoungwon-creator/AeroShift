using UnityEngine;

[CreateAssetMenu(menuName = "AeroFlip/Skin Data", fileName = "Skin Data")]
public class SkinData : ScriptableObject
{
    [SerializeField] private string skinId;
    [SerializeField] private string skinName;
    [SerializeField] private Mesh skinMesh;
    [SerializeField] private string requiredQuestId;
    [SerializeField] private bool unlockedByDefault;

    // 메뉴에서 실제 비행기 모델을 생성하기 위한 외형 프리팹.
    // 게임플레이용 Player 프리팹이 아닌 aircraft-a 등의 외형 프리팹을 연결한다.
    [SerializeField] private GameObject aircraftPrefab;

    public string SkinId => skinId;
    public string SkinName => skinName;
    public Mesh SkinMesh => skinMesh;
    public string RequiredQuestId => requiredQuestId;
    public bool UnlockedByDefault => unlockedByDefault;
    public GameObject AircraftPrefab => aircraftPrefab;
}
