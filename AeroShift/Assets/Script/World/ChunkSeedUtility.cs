public static class ChunkSeedUtility
{
    private static uint Hash(int seed, int chunkIndex, uint salt)
    {
        unchecked
        {
            uint value = (uint)seed ^ ((uint)chunkIndex * 0x9E3779B9u) ^ salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    public static int SelectNormalPrefab(int seed, int chunkIndex, int prefabCount)
    {
        if (prefabCount <= 0)
            return -1;

        return (int)(Hash(seed, chunkIndex, 0xA2B3C4D5u) % (uint)prefabCount);
    }

    public static bool ShouldRotate180(int seed, int chunkIndex)
    {
        return (Hash(seed, chunkIndex, 0x1234ABCDu) & 1u) != 0u;
    }
}
