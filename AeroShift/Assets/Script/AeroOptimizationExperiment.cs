
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
[RequireComponent(typeof(GCAllocationCapture))]
public sealed class AeroOptimizationExperiment : MonoBehaviour
{
    public enum ExperimentType
    {
        CloudSpawnRange,
        CloudGroupedMovement,
        MapPrepareCache,
        MapColliderCulling,
        CloudDistanceSpawn
    }

    [Header("Capture")]
    [SerializeField] private GCAllocationCapture gcCapture;

    [Header("Experiment")]
    [SerializeField]
    private ExperimentType experiment =
        ExperimentType.CloudDistanceSpawn;

    [SerializeField] private bool optimizationEnabled;
    [SerializeField, Min(1)] private int runNumber = 1;

    [Header("References")]
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private MapManager mapManager;

    public string ExperimentFilePrefix { get; private set; }
    public bool AppliedOptimizationState { get; private set; }

    private void Awake()
    {
        if (gcCapture == null)
            gcCapture = GetComponent<GCAllocationCapture>();

        if (cloudSpawner == null)
            cloudSpawner = FindFirstObjectByType<CloudSpawner>();

        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();

        if (gcCapture == null)
        {
            Debug.LogError("GCAllocationCapture가 없습니다.", this);
            enabled = false;
            return;
        }

        MonoBehaviour target = null;
        string fieldName = null;
        string category = null;
        string experimentName = null;

        switch (experiment)
        {
            case ExperimentType.CloudSpawnRange:
                target = cloudSpawner;
                fieldName = "restrictSpawnToCullRange";
                category = "Cloud";
                experimentName = "SpawnRange";
                break;

            case ExperimentType.CloudGroupedMovement:
                target = cloudSpawner;
                fieldName = "useGroupedMovement";
                category = "Cloud";
                experimentName = "GroupedMove";
                break;

            case ExperimentType.MapPrepareCache:
                target = mapManager;
                fieldName = "enableCachedPreparation";
                category = "Map";
                experimentName = "PrepareCache";
                break;

            case ExperimentType.MapColliderCulling:
                target = mapManager;
                fieldName = "enableProximityCollisionCulling";
                category = "Map";
                experimentName = "ColliderCulling";
                break;

            case ExperimentType.CloudDistanceSpawn:
                target = cloudSpawner;
                fieldName = "useDistanceBasedSpawn";
                category = "Cloud";
                experimentName = "DistanceSpawn";
                break;
        }

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Debug.LogError(
                "실험 대상 오브젝트를 찾지 못했습니다: " + experiment,
                this
            );

            gcCapture.enabled = false;
            enabled = false;
            return;
        }

        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        if (field == null || field.FieldType != typeof(bool))
        {
            Debug.LogError(
                "실험 옵션 필드가 없습니다: " + fieldName,
                this
            );

            gcCapture.enabled = false;
            enabled = false;
            return;
        }

        field.SetValue(target, optimizationEnabled);

        AppliedOptimizationState =
            (bool)field.GetValue(target);

        string state = AppliedOptimizationState ? "ON" : "OFF";

        ExperimentFilePrefix =
            "AeroShift_" +
            category + "_" +
            experimentName + "_" +
            state + "_Run" +
            Mathf.Max(1, runNumber).ToString("D2");

        if (!gcCapture.ConfigureScenarioName(ExperimentFilePrefix))
        {
            Debug.LogError(
                "GC Capture 시나리오 설정 실패",
                this
            );

            gcCapture.enabled = false;
            enabled = false;
            return;
        }

        Debug.Log(
            "최적화 실험 설정: " + ExperimentFilePrefix,
            this
        );
    }
}
