
using UnityEngine;

[CreateAssetMenu(fileName = "FlightConfig", menuName = "AeroShift/Flight Config")]
public class FlightConfig : ScriptableObject
{
    [Header("Movement Area")]
    public float horizontalLimit = 5f;
    public float verticalLimit = 3f;

    [Header("Movement")]
    public float smoothTime = 0.12f;

    [Header("Visual Tilt")]
    public float maxRoll = 25f;
    public float maxPitch = 15f;
    public float tiltSmooth = 8f;

    [Header("Input")]
    public float sensitivity = 1f;
}
