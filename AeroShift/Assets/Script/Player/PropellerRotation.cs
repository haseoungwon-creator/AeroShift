using UnityEngine;

public class PropellerRotation : MonoBehaviour
{
    [SerializeField] float rotationSpeed = 1500f;

    private void Update()
    {
        transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);
    }
}