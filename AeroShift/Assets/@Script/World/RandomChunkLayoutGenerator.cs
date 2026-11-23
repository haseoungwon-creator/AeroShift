using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RandomChunkLayoutGenerator
{
    public sealed class Chunk
    {
        public MapLayoutDefinition Layout;
        public int[] Variants;

        public Chunk(MapLayoutDefinition layout, int[] variants)
        {
            Layout = layout;
            Variants = variants;
        }
    }

    private sealed class StoneType
    {
        public string PrefabId;
        public int Variant;
        public Bounds LocalBounds;
        public float BaseScale;
    }

    private sealed class Shape
    {
        public List<Vector2> Polygon;
        public float Area;
        public float MinZ;
        public float MaxZ;
    }

    private readonly MapGenerationSettingsData settings;
    private readonly Dictionary<string, StoneType> stoneTypes = new Dictionary<string, StoneType>(StringComparer.Ordinal);
    private readonly Dictionary<int, Chunk> prepared = new Dictionary<int, Chunk>();
    private readonly Vector2 initialCenter;
    private readonly Vector2 playerExtents;
    private readonly float chunkLength;
    private Vector2 previousCenter;
    private Vector2 previousSize;
    private bool hasPrevious;

    public RandomChunkLayoutGenerator(
        MapGenerationSettingsData data,
        ResourceManager resources,
        PoolManager pool,
        Transform player,
        Collider[] colliders,
        Vector2 mapOrigin,
        float length)
    {
        settings = data;
        chunkLength = length;
        initialCenter = new Vector2(player.position.x - mapOrigin.x, player.position.y - mapOrigin.y);
        Vector2 extents = Vector2.one;

        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                Bounds bounds = collider.bounds;
                extents.x = Mathf.Max(extents.x, bounds.extents.x);
                extents.y = Mathf.Max(extents.y, bounds.extents.y);
            }
        }

        playerExtents = extents;
        GameObject[] prefabs = resources.LoadPrefabGroup(PoolId.MapStructure);

        foreach (MapGenerationStoneTypeData entry in data.stones)
        {
            if (!pool.TryGetVariantIndex(PoolId.MapStructure, entry.prefabId, out int variant))
                throw new ArgumentException("Stone prefab is not configured: " + entry.prefabId);

            GameObject prefab = null;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null && prefabs[i].name == entry.prefabId)
                {
                    prefab = prefabs[i];
                    break;
                }
            }

            if (prefab == null)
                throw new ArgumentException("Stone prefab is missing: " + entry.prefabId);

            MeshCollider collider = prefab.GetComponent<MeshCollider>();
            Mesh mesh = collider != null ? collider.sharedMesh : null;

            if (mesh == null)
            {
                MeshFilter filter = prefab.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }

            if (mesh == null)
                throw new ArgumentException("Stone mesh is missing: " + entry.prefabId);

            stoneTypes.Add(entry.prefabId, new StoneType
            {
                PrefabId = entry.prefabId,
                Variant = variant,
                LocalBounds = mesh.bounds,
                BaseScale = entry.baseScale
            });
        }

        Reset();
    }

    public void Reset()
    {
        prepared.Clear();
        previousCenter = initialCenter;
        previousSize = Vector2.zero;
        hasPrevious = false;
    }

    public void Release(int index)
    {
        prepared.Remove(index);
    }

    public Chunk GetOrCreate(int index, MapGenerationDifficultyData difficulty, int seed, float worldSpeed)
    {
        if (prepared.TryGetValue(index, out Chunk cached))
            return cached;

        System.Random random = new System.Random(unchecked(seed ^ (index * 73856093) ^ 0x27D4EB2D));
        Rect safeZone = CreateSafeZone(difficulty.safeZoneSizeRatio, worldSpeed, random);
        Rect clearanceZone = new Rect(
            safeZone.xMin - playerExtents.x - settings.safeZoneClearance,
            safeZone.yMin - playerExtents.y - settings.safeZoneClearance,
            safeZone.width + 2f * (playerExtents.x + settings.safeZoneClearance),
            safeZone.height + 2f * (playerExtents.y + settings.safeZoneClearance));

        List<Vector2> safePolygon = MakeRectangle(clearanceZone);
        List<MapStructurePlacement> placements = new List<MapStructurePlacement>(difficulty.maxStones);
        List<int> variants = new List<int>(difficulty.maxStones);
        List<Shape> shapes = new List<Shape>(difficulty.maxStones);
        int targetCount = random.Next(difficulty.minStones, difficulty.maxStones + 1);

        for (int i = 0; i < targetCount; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < settings.placementAttemptLimit; attempt++)
            {
                StoneType type = ChooseStone(difficulty.weights, random);
                float scaleFactor = Mathf.Lerp(settings.minScale, settings.maxScale, (float)random.NextDouble());
                if (attempt >= settings.placementAttemptLimit / 2)
                {
                    float shrink = (float)(attempt - settings.placementAttemptLimit / 2) /
                        Mathf.Max(1, settings.placementAttemptLimit - settings.placementAttemptLimit / 2 - 1);
                    scaleFactor = Mathf.Lerp(scaleFactor, settings.minScale, shrink);
                }

                float scale = type.BaseScale * scaleFactor;
                Vector3 position = new Vector3(
                    Mathf.Lerp(settings.areaMinX, settings.areaMaxX, (float)random.NextDouble()),
                    Mathf.Lerp(settings.areaMinY, settings.areaMaxY, (float)random.NextDouble()),
                    settings.stoneLocalZ);
                Vector3 rotation = new Vector3(
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f);
                Shape candidate = CreateShape(type.LocalBounds, position, rotation, scale);

                if (candidate.Area < 0.0001f ||
                    candidate.MinZ < settings.stoneLocalZ - chunkLength * 0.45f ||
                    candidate.MaxZ > settings.stoneLocalZ + chunkLength * 0.45f ||
                    IntersectionArea(candidate.Polygon, safePolygon) > 0.0001f)
                {
                    continue;
                }

                bool overlapsTooMuch = false;
                for (int j = 0; j < shapes.Count; j++)
                {
                    Shape other = shapes[j];
                    float overlap = IntersectionArea(candidate.Polygon, other.Polygon);
                    if (overlap > Mathf.Min(candidate.Area, other.Area) * settings.maxOverlapRatio)
                    {
                        overlapsTooMuch = true;
                        break;
                    }
                }

                if (overlapsTooMuch)
                    continue;

                placements.Add(new MapStructurePlacement
                {
                    prefabId = type.PrefabId,
                    position = position,
                    rotation = rotation,
                    scale = new Vector3(scale, scale, scale)
                });
                variants.Add(type.Variant);
                shapes.Add(candidate);
                placed = true;
                break;
            }

            if (!placed)
                continue;
        }

        MapLayoutDefinition layout = new MapLayoutDefinition
        {
            id = "random_" + index,
            structures = placements.ToArray()
        };

        Chunk result = new Chunk(layout, variants.ToArray());
        prepared.Add(index, result);
        previousCenter = safeZone.center;
        previousSize = safeZone.size;
        hasPrevious = true;
        return result;
    }

    private Rect CreateSafeZone(
    float sizeRatio,
    float worldSpeed,
    System.Random random)
    {
        float areaWidth = settings.areaMaxX - settings.areaMinX;
        float areaHeight = settings.areaMaxY - settings.areaMinY;

        float width = areaWidth * sizeRatio;
        float height = areaHeight * sizeRatio;

        float minCenterX = settings.areaMinX + width * 0.5f;
        float maxCenterX = settings.areaMaxX - width * 0.5f;
        float minCenterY = settings.areaMinY + height * 0.5f;
        float maxCenterY = settings.areaMaxY - height * 0.5f;

        float centerX;
        float centerY;

        if (!hasPrevious)
        {
            centerX = Mathf.Clamp(
                initialCenter.x, minCenterX, maxCenterX);

            centerY = Mathf.Clamp(
                initialCenter.y, minCenterY, maxCenterY);
        }
        else
        {
            float seconds = chunkLength / Mathf.Max(1f, worldSpeed);

            float maxDeltaX = Mathf.Min(
                areaWidth * settings.maxSafeZoneShiftRatio,
                settings.playerMoveSpeedX * seconds * 0.6f);

            float maxDeltaY = Mathf.Min(
                areaHeight * settings.maxSafeZoneShiftRatio,
                settings.playerMoveSpeedY * seconds * 0.6f);

            float lowX = Mathf.Max(
                minCenterX, previousCenter.x - maxDeltaX);

            float highX = Mathf.Min(
                maxCenterX, previousCenter.x + maxDeltaX);

            float lowY = Mathf.Max(
                minCenterY, previousCenter.y - maxDeltaY);

            float highY = Mathf.Min(
                maxCenterY, previousCenter.y + maxDeltaY);

            centerX = ChooseShiftedCenter(
                lowX, highX, previousCenter.x, random);

            centerY = ChooseShiftedCenter(
                lowY, highY, previousCenter.y, random);
        }

        return new Rect(
            centerX - width * 0.5f,
            centerY - height * 0.5f,
            width,
            height);
    }

    private StoneType ChooseStone(MapGenerationStoneWeight[] weights, System.Random random)
    {
        long total = 0;
        for (int i = 0; i < weights.Length; i++)
            total += weights[i].weight;

        double selection = random.NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            selection -= weights[i].weight;
            if (selection < 0)
                return stoneTypes[weights[i].prefabId];
        }

        return stoneTypes[weights[weights.Length - 1].prefabId];
    }

    private static Shape CreateShape(Bounds bounds, Vector3 position, Vector3 rotation, float scale)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Quaternion quaternion = Quaternion.Euler(rotation);
        List<Vector2> points = new List<Vector2>(8);
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 local = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 point = quaternion * (local * scale) + position;
                    points.Add(new Vector2(point.x, point.y));
                    minZ = Mathf.Min(minZ, point.z);
                    maxZ = Mathf.Max(maxZ, point.z);
                }
            }
        }

        List<Vector2> hull = ConvexHull(points);
        return new Shape
        {
            Polygon = hull,
            Area = PolygonArea(hull),
            MinZ = minZ,
            MaxZ = maxZ
        };
    }

    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        points.Sort((a, b) =>
        {
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        });
        List<Vector2> lower = new List<Vector2>(8);
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 point = points[i];
            while (lower.Count >= 2 &&
                Cross(lower[lower.Count - 1] - lower[lower.Count - 2], point - lower[lower.Count - 1]) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }

        List<Vector2> upper = new List<Vector2>(8);
        for (int i = points.Count - 1; i >= 0; i--)
        {
            Vector2 point = points[i];
            while (upper.Count >= 2 &&
                Cross(upper[upper.Count - 1] - upper[upper.Count - 2], point - upper[upper.Count - 1]) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static List<Vector2> MakeRectangle(Rect rectangle)
    {
        return new List<Vector2>(4)
        {
            new Vector2(rectangle.xMin, rectangle.yMin),
            new Vector2(rectangle.xMax, rectangle.yMin),
            new Vector2(rectangle.xMax, rectangle.yMax),
            new Vector2(rectangle.xMin, rectangle.yMax)
        };
    }

    private static float IntersectionArea(List<Vector2> subject, List<Vector2> clip)
    {
        List<Vector2> polygon = subject;
        for (int i = 0; i < clip.Count; i++)
        {
            Vector2 a = clip[i];
            Vector2 b = clip[(i + 1) % clip.Count];
            List<Vector2> output = new List<Vector2>();
            if (polygon.Count == 0)
                return 0f;

            Vector2 previous = polygon[polygon.Count - 1];
            bool wasInside = Cross(b - a, previous - a) >= -0.0001f;

            for (int j = 0; j < polygon.Count; j++)
            {
                Vector2 point = polygon[j];
                bool inside = Cross(b - a, point - a) >= -0.0001f;
                if (inside != wasInside)
                {
                    float from = Cross(b - a, previous - a);
                    float to = Cross(b - a, point - a);
                    float denominator = from - to;
                    if (Mathf.Abs(denominator) > 0.0000001f)
                        output.Add(Vector2.LerpUnclamped(previous, point, from / denominator));
                }

                if (inside)
                    output.Add(point);

                previous = point;
                wasInside = inside;
            }

            polygon = output;
        }

        return PolygonArea(polygon);
    }

    private static float PolygonArea(List<Vector2> polygon)
    {
        if (polygon.Count < 3)
            return 0f;

        float twiceArea = 0f;
        for (int i = 0; i < polygon.Count; i++)
            twiceArea += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        return Mathf.Abs(twiceArea) * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float ChooseShiftedCenter(
     float min,
     float max,
     float previous,
     System.Random random)
    {
        if (min >= max)
            return Mathf.Clamp(previous, min, max);

        float leftDistance = Mathf.Max(0f, previous - min);
        float rightDistance = Mathf.Max(0f, max - previous);

        float maxDistance = Mathf.Max(
            leftDistance, rightDistance);

        if (maxDistance <= 0.001f)
            return Mathf.Clamp(previous, min, max);

        float minimumShift = maxDistance * 0.6f;

        bool canMoveLeft = leftDistance >= minimumShift;
        bool canMoveRight = rightDistance >= minimumShift;

        if (canMoveLeft && canMoveRight)
        {
            if (random.Next(2) == 0)
            {
                return Mathf.Lerp(
                    min,
                    previous - minimumShift,
                    (float)random.NextDouble());
            }

            return Mathf.Lerp(
                previous + minimumShift,
                max,
                (float)random.NextDouble());
        }

        if (canMoveLeft)
        {
            return Mathf.Lerp(
                min,
                previous - minimumShift,
                (float)random.NextDouble());
        }

        if (canMoveRight)
        {
            return Mathf.Lerp(
                previous + minimumShift,
                max,
                (float)random.NextDouble());
        }

        return Mathf.Clamp(previous, min, max);
    }
}
