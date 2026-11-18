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

    private IEnumerator LoadSelectedMeshAsync()
    {
        if (selectedAircraft == null || meshFilter == null)
            yield break;

        string id = selectedAircraft.AircraftId;
        int version = meshRequestVersion;

        ResourceRequest request = Resources.LoadAsync<GameObject>("Aircraft/" + id);
        yield return request;

        if (version != meshRequestVersion)
            yield break;

        GameObject model = request.asset as GameObject;

        ApplyMesh(model);
    }

    public bool ApplySelected()
    {
        if (selectedAircraft == null)
            return false;

        return ChangeMesh(selectedAircraft.AircraftId);
    }

    public bool ChangeMesh(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        meshRequestVersion++;

        GameObject model = Resources.Load<GameObject>("Aircraft/" + id);

        return ApplyMesh(model);
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
