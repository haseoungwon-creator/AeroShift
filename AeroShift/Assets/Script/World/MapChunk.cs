using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapChunk : MonoBehaviour
{
    public int ChunkIndex { get; private set; }
    public float StartZ { get; private set; }
    public float EndZ { get; private set; }
    public bool IsSafe { get; private set; }
    public bool Rotated180 { get; private set; }

    public void SetChunkData(int index, float startZ, float length, bool isSafe, bool rotated180)
    {
        ChunkIndex = index;
        StartZ = startZ;
        EndZ = startZ + length;
        IsSafe = isSafe;
        Rotated180 = rotated180;
    }
}
