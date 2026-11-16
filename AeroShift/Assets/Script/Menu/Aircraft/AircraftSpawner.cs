using System.Collections;
using UnityEngine;

public class AircraftSpawner : MonoBehaviour
{
    [SerializeField] private SelectedAircraftSO selectedAircraft;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshCollider meshCollider;

    private int meshRequestVersion;

    public SelectedAircraftSO SelectedAircraft
    {
        get { return selectedAircraft; }
    }

    private void Awake()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshCollider == null)
            meshCollider = GetComponent<MeshCollider>();

        if (selectedAircraft != null)
            selectedAircraft.LoadSaved();
    }

    private void Start()
    {
        StartCoroutine(LoadSelectedMeshAsync());
    }

    // Main 씬 진입 시 선택한 모델을 비동기로 로드
    private IEnumerator LoadSelectedMeshAsync()
    {
        if (selectedAircraft == null || meshFilter == null)
            yield break;

        string id = selectedAircraft.AircraftId;
        int version = meshRequestVersion;

        ResourceRequest request = Resources.LoadAsync<GameObject>("Aircraft/" + id);
        yield return request;

        // 사용자가 이미 다른 스킨을 미리 보고 있다면 이전 로딩 결과 무시
        if (version != meshRequestVersion)
            yield break;

        GameObject model = request.asset as GameObject;

        if (!ApplyMesh(model))
            Debug.LogWarning("비행기 Mesh 로드 실패: " + id, this);
    }

    // SO에 확정된 ID의 비행기로 복구
    public bool ApplySelected()
    {
        if (selectedAircraft == null)
            return false;

        return ChangeMesh(selectedAircraft.AircraftId);
    }

    // 메뉴 미리보기용: SO 데이터는 변경하지 않음
    public bool ChangeMesh(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        meshRequestVersion++;

        GameObject model = Resources.Load<GameObject>("Aircraft/" + id);

        if (!ApplyMesh(model))
        {
            Debug.LogWarning("비행기 Mesh 로드 실패: " + id, this);
            return false;
        }

        return true;
    }

    private bool ApplyMesh(GameObject model)
    {
        if (model == null || meshFilter == null)
            return false;

        MeshFilter modelFilter = model.GetComponentInChildren<MeshFilter>(true);

        if (modelFilter == null || modelFilter.sharedMesh == null)
            return false;

        Mesh mesh = modelFilter.sharedMesh;
        meshFilter.sharedMesh = mesh;

        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        return true;
    }
}
