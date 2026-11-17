using UnityEngine;

public enum PlayerModes
{
    Mode3D,
    Mode2D
}
public class PlayerMode : MonoBehaviour
{
    [SerializeField] PlayerModes currentMode = PlayerModes.Mode3D;

    public PlayerModes CurrentMode => currentMode;

    public void SetMode(PlayerModes mode)
    {
        currentMode = mode;
    }

    public void ResetMode()
    {
        currentMode = PlayerModes.Mode3D;
    }

    public bool Is3D()
    {
        return currentMode == PlayerModes.Mode3D;
    }

    public bool IsMode2D()
    {
        return currentMode == PlayerModes.Mode2D;
    }
}
