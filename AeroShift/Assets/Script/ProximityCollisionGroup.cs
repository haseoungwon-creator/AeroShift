using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProximityCollisionGroup : MonoBehaviour
{
    private Collider[] trackedColliders;
    private bool[] originalEnabledStates;
    private int configuredLayerMask;
    private bool initialized;
    private bool collisionsEnabled = true;

    public int TrackedColliderCount => trackedColliders != null ? trackedColliders.Length : 0;
    public bool CollisionsEnabled => collisionsEnabled;

    public void Initialize(LayerMask obstacleLayers)
    {
        int layerMask = obstacleLayers.value;

        if (initialized && configuredLayerMask == layerMask)
            return;

        if (initialized)
            RestoreOriginalStates();

        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        int count = 0;

        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider current = allColliders[i];

            if (current != null && ((1 << current.gameObject.layer) & layerMask) != 0)
                count++;
        }

        trackedColliders = new Collider[count];
        originalEnabledStates = new bool[count];
        int index = 0;

        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider current = allColliders[i];

            if (current == null || ((1 << current.gameObject.layer) & layerMask) == 0)
                continue;

            trackedColliders[index] = current;
            originalEnabledStates[index] = current.enabled;
            index++;
        }

        configuredLayerMask = layerMask;
        initialized = true;
        collisionsEnabled = true;
    }

    public bool SetCollisionsEnabled(bool enable)
    {
        if (!initialized || collisionsEnabled == enable)
            return false;

        for (int i = 0; i < trackedColliders.Length; i++)
        {
            Collider current = trackedColliders[i];

            if (current == null)
                continue;

            bool desired = enable && originalEnabledStates[i];

            if (current.enabled != desired)
                current.enabled = desired;
        }

        collisionsEnabled = enable;
        return true;
    }

    private void RestoreOriginalStates()
    {
        if (trackedColliders == null)
            return;

        for (int i = 0; i < trackedColliders.Length; i++)
        {
            Collider current = trackedColliders[i];

            if (current != null && current.enabled != originalEnabledStates[i])
                current.enabled = originalEnabledStates[i];
        }

        collisionsEnabled = true;
    }

    private void OnDisable()
    {
        if (initialized)
            RestoreOriginalStates();
    }
}
