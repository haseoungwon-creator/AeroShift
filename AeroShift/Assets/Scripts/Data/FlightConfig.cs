
using UnityEngine;

[CreateAssetMenu(fileName = "FlightConfig", menuName = "AeroShift/Flight Config")]
public class FlightConfig : ScriptableObject
{
    [Header("Movement Area")]
    public float horizontalLimit = 5f;
    public float verticalLimit = 3f;

    [Header("Movement")]
    public float smoothTime = 0.15f;
    public float sensitivity = 1f;
    public float baseSpeed = 12f;
    public float maxSpeed = 24f;
    public float minSpeed = 8f;

    [Header("Flight Rotation")]
    public float maxYaw = 25f;
    public float maxPitch = 20f;
    public float rotationSmooth = 4f;
    public float turnSpeed = 70f;

    [Header("Acceleration")]
    public float acceleration = 4f;

    [Header("Visual Banking")]
    public float maxRoll = 35f;
    public float rollSmooth = 5f;
}
