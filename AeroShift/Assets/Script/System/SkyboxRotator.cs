using UnityEngine;

public class SkyboxRotator : MonoBehaviour
{
    [SerializeField] float rotationSpeed = 1f;

    private void Update()
    {
        RenderSettings.skybox.SetFloat("_Rotation", Time.time * rotationSpeed);
    }
}