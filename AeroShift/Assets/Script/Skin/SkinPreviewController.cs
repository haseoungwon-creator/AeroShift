using UnityEngine;

public class SkinPreviewController : MonoBehaviour
{
    [SerializeField] SkinManager skinManager;
    [SerializeField] MeshFilter previewMeshFilter;
    [SerializeField] float rotationSpeed = 30f;

    private bool isPreviewing;

    private void Update()
    {
        if (!isPreviewing)
            return;

        transform.Rotate(
            Vector3.up,
            rotationSpeed * Time.deltaTime,
            Space.World);
    }

    public void SetPreviewMode(bool value)
    {
        isPreviewing = value;

        if (!value)
        {
            transform.rotation = Quaternion.identity;
        }
    }

    public void UpdatePreview()
    {
        if (skinManager == null)
            return;

        if (previewMeshFilter == null)
            return;

        if (!skinManager.IsCurrentSkinUnlocked())
            return;

        SkinData skin = skinManager.SelectedSkin;

        if (skin == null || skin.SkinMesh == null)
            return;

        previewMeshFilter.sharedMesh = skin.SkinMesh;
    }

    public void NextSkin()
    {
        if (skinManager == null)
            return;

        skinManager.SelectNext();
        UpdatePreview();
    }

    public void PreviousSkin()
    {
        if (skinManager == null)
            return;

        skinManager.SelectPrevious();
        UpdatePreview();
    }

    public void ConfirmSkin()
    {
        if (skinManager == null)
            return;

        skinManager.ConfirmSkin();
        UpdatePreview();
    }

    public void ResetPreview()
    {
        if (skinManager == null)
            return;

        skinManager.ResetSelection();
        UpdatePreview();

        SetPreviewMode(false);
    }
}