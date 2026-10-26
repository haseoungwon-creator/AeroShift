
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform model;
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 2f, -8f);
    [SerializeField] private Vector3 rotationOffset = new Vector3(8f, 0f, 0f);
    [SerializeField] private float positionSmoothTime = 0.2f;
    [SerializeField] private float rotationSmooth = 3f;
    [SerializeField] private float pitchInfluence = 0.2f;
    [SerializeField] private float yawInfluence = 0.2f;
    [SerializeField] private float rollInfluence = 0.25f;

    private Vector3 positionVelocity;

    private void LateUpdate()
    {
        if (target == null)
            return;

        UpdatePosition();
        UpdateRotation();
    }

    private void UpdatePosition()
    {
        Vector3 targetPosition = target.position + positionOffset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref positionVelocity,
            positionSmoothTime
        );
    }

    private void UpdateRotation()
    {
        Vector3 targetAngles = target.eulerAngles;

        float pitch = Mathf.DeltaAngle(0f, targetAngles.x) * pitchInfluence;
        float yaw = Mathf.DeltaAngle(0f, targetAngles.y) * yawInfluence;
        float roll = 0f;

        if (model != null)
        {
            roll = Mathf.DeltaAngle(
                0f,
                model.localEulerAngles.z
            ) * rollInfluence;
        }

        Quaternion targetRotation = Quaternion.Euler(
            rotationOffset.x + pitch,
            rotationOffset.y + yaw,
            rotationOffset.z + roll
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSmooth
        );
    }
}
