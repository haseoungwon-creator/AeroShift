using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// AeroShift 개별 최적화 실험 관리자 (개별 실험 전용).
/// 실험 선택 -> bool ON/OFF -> 워밍업 -> 프레임/CPU/물리/GC/메모리 측정 -> CSV 저장.
/// 이전 AeroOptimizationExperiment + GCAllocationCapture를 이 스크립트 하나로 대체합니다.
/// </summary>
[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class AeroOptimizationExperiment : MonoBehaviour
{
    public enum ExperimentType
    {
        CloudSpawnRange,
        CloudGroupedMovement,
        MapPrepareCache,
        MapColliderCulling,
        CloudDistanceSpawn,

        // 향후 기능: Inspector에서 연결할 대상 컴포넌트 및 bool 필드명을 지정합니다.
        MapCustom,
        MissileCustom
    }

    private enum ExperimentCategory { Cloud, Map, Missile }
    private enum CaptureState { Waiting, WarmingUp, Capturing, Finished }

    [Header("1. 최적화 실험 선택")]
    [Tooltip("어떤 최적화 기능을 시험할지 선택합니다.")]
    [SerializeField] private ExperimentType experiment = ExperimentType.MapPrepareCache;

    [Tooltip("OFF = 기존 방식, ON = 개선 방식. Awake에서 실제 대상 필드에 적용합니다.")]
    [SerializeField] private bool optimizationEnabled;

    [Tooltip("반복 측정 번호. 예: 1 -> Run01")]
    [SerializeField, Min(1)] private int runNumber = 1;

    [Header("2. 실험 대상 참조")]
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private PoolManager poolManager;

    [Header("3. 향후 맵/미사일 실험 확장")]
    [Tooltip("MapCustom에서 ON/OFF를 적용할 스크립트. 없으면 MapManager 사용")]
    [SerializeField] private MonoBehaviour customMapTarget;
    [Tooltip("MapCustom의 실제 bool 필드명. 예: useObjectPooling")]
    [SerializeField] private string mapBooleanFieldName = "";
    [Tooltip("MapCustom 결과 파일에 사용할 실험 이름")]
    [SerializeField] private string mapExperimentName = "Custom";

    [Tooltip("MissileCustom에서 ON/OFF를 적용할 미사일 관련 스크립트")]
    [SerializeField] private MonoBehaviour missileTarget;
    [Tooltip("MissileCustom의 실제 bool 필드명. 코드에 존재하는 이름을 정확히 적습니다")]
    [SerializeField] private string missileBooleanFieldName = "";
    [Tooltip("MissileCustom 결과 파일에 사용할 실험 이름(영문/숫자 권장)")]
    [SerializeField] private string missileExperimentName = "Custom";

    [Header("4. 개별 측정 시간")]
    [Tooltip("게임이 Playing 상태가 되면 자동으로 측정 시작")]
    [SerializeField] private bool startAutomaticallyWhenPlaying = true;
    [Tooltip("GameManager.CurrentState == Playing인 경우에만 자동 측정. 수동 시작은 예외")]
    [SerializeField] private bool waitForGamePlaying = true;
    [Tooltip("측정 시작 전 초기화 및 로딩 영향을 제외할 시간(초)")]
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [Tooltip("본 측정 시간(초)")]
    [SerializeField, Min(1f)] private float captureSeconds = 30f;
    [Tooltip("메모리 사용 제한을 위한 최대 프레임 기록 개수")]
    [SerializeField, Min(1)] private int maxRecordedFrames = 36000;

    [Header("5. Unity Profiler 상세 기록 (선택)")]
    [Tooltip("true면 Unity Profiler에서 불러올 .raw 데이터도 저장")]
    [SerializeField] private bool saveProfilerRaw;
    [Tooltip("GC 할당 발생 위치의 호출 스택 기록. 성능 오버헤드가 큼")]
    [SerializeField] private bool captureAllocationCallstacks;
    [Tooltip("Profiler 내부 버퍼 크기(MiB)")]
    [SerializeField, Range(16, 1024)] private int profilerBufferMiB = 128;

    [Header("6. 구름/맵/미사일 부가 지표")]
    [Tooltip("구름 개수 및 Pool 상태, 맵/미사일 개수(연결한 경우)를 CSV에 별도 저장")]
    [SerializeField] private bool captureFeatureMetrics = true;
    [Tooltip("부가 지표를 몇 프레임마다 읽을지 결정 (기본 10)")]
    [SerializeField, Min(1)] private int featureSampleIntervalFrames = 10;

    [Tooltip("맵 활성 개수를 제공하는 public/private int 필드 또는 프로퍼티 이름 (선택)")]
    [SerializeField] private string mapActiveCountMemberName = "";
    [Tooltip("미사일 활성 개수를 제공하는 public/private int 필드 또는 프로퍼티 이름 (선택)")]
    [SerializeField] private string missileActiveCountMemberName = "";
    [Tooltip("PoolId enum의 맵 항목 이름. 없거나 비워두면 맵 풀 지표는 -1")]
    [SerializeField] private string mapPoolIdName = "";
    [Tooltip("PoolId enum의 미사일 항목 이름. 없거나 비워두면 미사일 풀 지표는 -1")]
    [SerializeField] private string missilePoolIdName = "";

    public string ExperimentFilePrefix { get; private set; }
    public bool AppliedOptimizationState { get; private set; }
    public bool IsCapturing { get { return state == CaptureState.Capturing; } }

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private const BindingFlags AnyInstanceField =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;
    private const long InvalidMetric = -1;

    private struct FrameSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public float FrameMilliseconds;
        public long MainThreadNanoseconds;
        public long RenderThreadNanoseconds;
        public long PhysicsSimulateNanoseconds;
        public long PhysicsProcessingNanoseconds;
        public long PhysicsSyncColliderNanoseconds;
        public long GCAllocatedBytes;
        public long GCUsedBytes;
        public long SystemUsedBytes;
    }

    private struct FeatureSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public int ActiveCount;
        public int CloudInitialTargetCount;
        public int CloudInitialPlacedCount;
        public float CloudSpawnDistance;
        public int CloudSpawnRangeLimited;
        public int CloudRentFailures;
        public int PoolCreated;
        public int PoolActive;
        public int PoolPeakActive;
        public int PoolAvailable;
        public int PoolFailedRent;
        public int PoolRuntimeCreation;
    }

    // 맵 실험에서만 기록하는 세부 지표. 기존 FeatureSample과 CSV 형식은 유지합니다.
    private struct MapSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public int ActiveChunks;
        public int NextChunkIndex;
        public int Spawned;
        public int Recycled;
        public int PrepareCreations;
        public int FailedSpawns;
        public int PrepareRebuilds;
        public int PreparePasses;
        public int CollisionStateChanges;
        public int CoverageReady;
        public int NormalActive;
        public int SafeActive;
        public int NormalAvailable;
        public int SafeAvailable;
    }

    // 씬 재로드/자동 반복 기능은 사용하지 않습니다.
    private readonly List<MapSample> mapSamples = new List<MapSample>(512);

    private readonly List<FrameSample> frames = new List<FrameSample>(4096);
    private readonly List<FeatureSample> featureSamples = new List<FeatureSample>(512);

    private CaptureState state = CaptureState.Waiting;
    private ExperimentCategory category;
    private string experimentName;
    private bool manualCapture;
    private float warmupStart;
    private float captureStart;
    private int lastFeatureSampleFrame = -1;
    private string outputDirectory;
    private string outputPrefix;
    private string profilerRawPath;

    private ProfilerRecorder gcAllocatedRecorder;
    private ProfilerRecorder gcUsedRecorder;
    private ProfilerRecorder systemUsedRecorder;
    private ProfilerRecorder mainThreadRecorder;
    // CPU 시간(나노초). 사용 불가능한 마커는 -1로 기록합니다.
    private ProfilerRecorder renderThreadRecorder;
    private ProfilerRecorder physicsSimulateRecorder;
    private ProfilerRecorder physicsProcessingRecorder;
    private ProfilerRecorder physicsSyncColliderRecorder;

    private bool profilerStateSaved;
    private bool previousProfilerEnabled;
    private bool previousBinaryLogEnabled;
    private bool previousCallstacksEnabled;
    private string previousLogFile;
    private int previousProfilerMemory;

    private void Awake()
    {
        // 최적화 옵션은 CloudSpawner/MapManager의 초기화보다 먼저 적용하려는 목적입니다.
        // 상대 스크립트의 실행 순서가 -900보다 빠르면 별도 조정해야 합니다.
        if (cloudSpawner == null) cloudSpawner = FindFirstObjectByType<CloudSpawner>();
        if (mapManager == null) mapManager = FindFirstObjectByType<MapManager>();
        if (poolManager == null) poolManager = FindFirstObjectByType<PoolManager>();

        MonoBehaviour target;
        string boolFieldName;
        if (!ResolveExperiment(out target, out boolFieldName))
        {
            FailSetup("실험 종류/필드 설정이 올바르지 않습니다.");
            return;
        }

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            FailSetup("실험 대상이 없거나 비활성 상태입니다: " + experiment);
            return;
        }

        FieldInfo option = FindBoolField(target.GetType(), boolFieldName);
        if (option == null)
        {
            FailSetup("bool 필드가 없습니다: " + boolFieldName +
                      " (" + target.GetType().Name + ")");
            return;
        }

        try
        {
            option.SetValue(target, optimizationEnabled);
            AppliedOptimizationState = (bool)option.GetValue(target);
        }
        catch (Exception exception)
        {
            FailSetup("최적화 옵션 적용 실패: " + exception.Message);
            return;
        }

        ExperimentFilePrefix = "AeroShift_" + category + "_" +
            CleanFileName(experimentName) + "_" +
            (AppliedOptimizationState ? "ON" : "OFF") + "_Run" +
            Mathf.Max(1, runNumber).ToString("D2", Culture);

        Debug.Log("[AeroExperiment] 적용 완료: " + ExperimentFilePrefix +
                  " / 변경 필드: " + boolFieldName, this);
    }

    /// <summary>실험별 대상과 실제 bool 필드 이름을 한 곳에서 결정합니다.</summary>
    private void FailSetup(string message)
    {
        Debug.LogError("[AeroExperiment] " + message, this);
        enabled = false;
    }

    /// <summary>실험별 대상과 실제 bool 필드 이름을 한 곳에서 결정합니다.</summary>
    private bool ResolveExperiment(out MonoBehaviour target, out string fieldName)
    {
        target = null;
        fieldName = null;
        switch (experiment)
        {
            case ExperimentType.CloudSpawnRange:
                category = ExperimentCategory.Cloud;
                experimentName = "SpawnRange";
                target = cloudSpawner;
                fieldName = "restrictSpawnToCullRange";
                break;

            case ExperimentType.CloudGroupedMovement:
                category = ExperimentCategory.Cloud;
                experimentName = "GroupedMove";
                target = cloudSpawner;
                fieldName = "useGroupedMovement";
                break;

            case ExperimentType.MapPrepareCache:
                category = ExperimentCategory.Map;
                experimentName = "PrepareCache";
                target = mapManager;
                fieldName = "enableCachedPreparation";
                break;

            case ExperimentType.MapColliderCulling:
                category = ExperimentCategory.Map;
                experimentName = "ColliderCulling";
                target = mapManager;
                fieldName = "enableProximityCollisionCulling";
                break;

            case ExperimentType.CloudDistanceSpawn:
                category = ExperimentCategory.Cloud;
                experimentName = "DistanceSpawn";
                target = cloudSpawner;
                fieldName = "useDistanceBasedSpawn";
                break;

            case ExperimentType.MapCustom:
                category = ExperimentCategory.Map;
                experimentName = string.IsNullOrWhiteSpace(mapExperimentName)
                    ? "Custom" : mapExperimentName.Trim();
                target = customMapTarget != null ? customMapTarget : mapManager;
                fieldName = mapBooleanFieldName.Trim();
                break;

            case ExperimentType.MissileCustom:
                category = ExperimentCategory.Missile;
                experimentName = string.IsNullOrWhiteSpace(missileExperimentName)
                    ? "Custom" : missileExperimentName.Trim();
                target = missileTarget;
                fieldName = missileBooleanFieldName.Trim();
                break;

            default:
                Debug.LogError("[AeroExperiment] 정의되지 않은 실험입니다.", this);
                return false;
        }

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            Debug.LogError("[AeroExperiment] 최적화 bool 필드명을 입력하세요: " + experiment, this);
            return false;
        }
        return true;
    }

    private static FieldInfo FindBoolField(Type type, string name)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            FieldInfo field = t.GetField(name, AnyInstanceField);
            if (field != null && field.FieldType == typeof(bool) && !field.IsStatic &&
                !field.IsInitOnly && !field.IsLiteral)
                return field;
        }
        return null;
    }

    private void Update()
    {
        if (state == CaptureState.Waiting)
        {
            if (startAutomaticallyWhenPlaying &&
                (!waitForGamePlaying || IsGamePlaying()))
                EnterWarmup(false);
            return;
        }

        if (state == CaptureState.WarmingUp)
        {
            if (!manualCapture && waitForGamePlaying && !IsGamePlaying())
            {
                state = CaptureState.Waiting;
                return;
            }
            if (Time.unscaledTime - warmupStart >= Mathf.Max(0f, warmupSeconds))
                BeginCapture();
            return;
        }

        if (state != CaptureState.Capturing) return;
        if (!manualCapture && waitForGamePlaying && !IsGamePlaying())
        {
            FinishCapture(false);
            return;
        }
        if (Time.unscaledTime - captureStart >= Mathf.Max(1f, captureSeconds))
            FinishCapture(true);
    }

    private void LateUpdate()
    {
        if (state != CaptureState.Capturing) return;

        frames.Add(new FrameSample
        {
            Frame = Time.frameCount,
            ElapsedSeconds = Time.unscaledTime - captureStart,
            FrameMilliseconds = Time.unscaledDeltaTime * 1000f,
            // LastValue는 '마지막으로 완료된 프레임'의 값입니다.
            MainThreadNanoseconds = ReadLastValue(mainThreadRecorder),
            RenderThreadNanoseconds = ReadLastValue(renderThreadRecorder),
            PhysicsSimulateNanoseconds = ReadLastValue(physicsSimulateRecorder),
            PhysicsProcessingNanoseconds = ReadLastValue(physicsProcessingRecorder),
            PhysicsSyncColliderNanoseconds = ReadLastValue(physicsSyncColliderRecorder),
            GCAllocatedBytes = ReadLastValue(gcAllocatedRecorder),
            GCUsedBytes = ReadLastValue(gcUsedRecorder),
            SystemUsedBytes = ReadLastValue(systemUsedRecorder)
        });

        if (captureFeatureMetrics &&
            (lastFeatureSampleFrame < 0 ||
             Time.frameCount - lastFeatureSampleFrame >= Mathf.Max(1, featureSampleIntervalFrames)))
        {
            RecordFeatureSample();
        }

        if (frames.Count >= Mathf.Max(1, maxRecordedFrames))
            FinishCapture(false);
    }

    [ContextMenu("실험 측정 수동 시작")]
    public void StartCaptureNow()
    {
        if (!Application.isPlaying || !enabled || state == CaptureState.Capturing) return;
        EnterWarmup(true);
    }

    [ContextMenu("실험 측정 중지 및 CSV 저장")]
    public void StopCapture()
    {
        if (state == CaptureState.Capturing) FinishCapture(false);
        else if (state == CaptureState.WarmingUp) state = CaptureState.Finished;
    }

    private void EnterWarmup(bool isManual)
    {
        manualCapture = isManual;
        warmupStart = Time.unscaledTime;
        state = CaptureState.WarmingUp;
    }

    private static bool IsGamePlaying()
    {
        GameManager manager = GameManager.Instance;
        return manager != null && manager.CurrentState == GameStates.Playing;
    }

    private void BeginCapture()
    {
        frames.Clear();
        featureSamples.Clear();
        mapSamples.Clear();
        lastFeatureSampleFrame = -1;

        try
        {
            // 기존과 동일: AeroBenchmarks/Experiments/{분류}/{실험}/{ON|OFF}/RunXX/
            outputDirectory = Path.Combine(GetExperimentsRoot(),
                category.ToString(), CleanFileName(experimentName),
                AppliedOptimizationState ? "ON" : "OFF",
                "Run" + Mathf.Max(1, runNumber).ToString("D2", Culture));
            Directory.CreateDirectory(outputDirectory);

            outputPrefix = ExperimentFilePrefix + "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Culture);
            profilerRawPath = Path.Combine(outputDirectory, outputPrefix + "_profile.raw");

            if (saveProfilerRaw)
            {
                SaveProfilerSettings();
                Profiler.enabled = false;
                Profiler.maxUsedMemory = Mathf.Clamp(profilerBufferMiB, 16, 1024) * 1024 * 1024;
                Profiler.logFile = profilerRawPath;
                Profiler.enableBinaryLog = true;
                Profiler.enableAllocationCallstacks = captureAllocationCallstacks;
                Profiler.enabled = true;
            }

            gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            gcUsedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Used Memory", 1);
            systemUsedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "System Used Memory", 1);
            mainThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "Main Thread", 1);

            // 기본 ProfilerRecorder 옵션은 프레임에 발생한 같은 마커를 합산합니다.
            // 메인 스레드 시간은 전체 CPU 사용률(%)과 동일하지 않습니다.
            renderThreadRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "Render Thread", 1);
            physicsSimulateRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Physics, "Physics.Simulate", 1);
            physicsProcessingRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Physics, "Physics.Processing", 1);
            physicsSyncColliderRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Physics, "Physics.SyncColliderTransform", 1);

            captureStart = Time.unscaledTime;
            state = CaptureState.Capturing;
            if (captureFeatureMetrics) RecordFeatureSample();
        }
        catch (Exception exception)
        {
            DisposeRecorders();
            RestoreProfilerSettings();
            state = CaptureState.Finished;
            Debug.LogException(exception, this);
        }
    }

    private void RecordFeatureSample()
    {
        FeatureSample sample = new FeatureSample
        {
            Frame = Time.frameCount,
            ElapsedSeconds = Time.unscaledTime - captureStart,
            ActiveCount = -1,
            CloudInitialTargetCount = -1,
            CloudInitialPlacedCount = -1,
            CloudSpawnDistance = -1f,
            CloudSpawnRangeLimited = -1,
            CloudRentFailures = -1,
            PoolCreated = -1,
            PoolActive = -1,
            PoolPeakActive = -1,
            PoolAvailable = -1,
            PoolFailedRent = -1,
            PoolRuntimeCreation = -1
        };

        MonoBehaviour statsTarget = null;
        string activeMemberName = null;
        string poolIdName = null;

        if (category == ExperimentCategory.Cloud)
        {
            statsTarget = cloudSpawner;
            poolIdName = "Cloud";
            if (cloudSpawner != null)
            {
                sample.ActiveCount = cloudSpawner.ActiveCloudCount;
                sample.CloudInitialTargetCount = cloudSpawner.InitialTargetCount;
                sample.CloudInitialPlacedCount = cloudSpawner.InitialPlacedCount;
                sample.CloudSpawnDistance = cloudSpawner.EffectiveSpawnDistance;
                sample.CloudSpawnRangeLimited = cloudSpawner.SpawnRangeLimited ? 1 : 0;
                sample.CloudRentFailures = cloudSpawner.CloudRentFailures;
            }
        }
        else if (category == ExperimentCategory.Map)
        {
            statsTarget = customMapTarget != null && experiment == ExperimentType.MapCustom
                ? customMapTarget : mapManager;
            activeMemberName = mapActiveCountMemberName;
            poolIdName = mapPoolIdName;
        }
        else
        {
            statsTarget = missileTarget;
            activeMemberName = missileActiveCountMemberName;
            poolIdName = missilePoolIdName;
        }

        if (category != ExperimentCategory.Cloud && statsTarget != null &&
            !string.IsNullOrWhiteSpace(activeMemberName))
        {
            sample.ActiveCount = ReadIntMember(statsTarget, activeMemberName.Trim());
        }

        PoolId poolId;
        if (poolManager != null && !string.IsNullOrWhiteSpace(poolIdName) &&
            Enum.TryParse(poolIdName.Trim(), true, out poolId) &&
            Enum.IsDefined(typeof(PoolId), poolId))
        {
            sample.PoolCreated = poolManager.GetCreatedCount(poolId);
            sample.PoolActive = poolManager.GetActiveCount(poolId);
            sample.PoolPeakActive = poolManager.GetPeakActiveCount(poolId);
            sample.PoolAvailable = poolManager.GetAvailableCount(poolId);
            sample.PoolFailedRent = poolManager.GetFailedRentCount(poolId);
            sample.PoolRuntimeCreation = poolManager.GetRuntimeCreationCount(poolId);
        }

        featureSamples.Add(sample);
        if (category == ExperimentCategory.Map && mapManager != null)
            RecordMapSample();
        lastFeatureSampleFrame = Time.frameCount;
    }

    private void RecordMapSample()
    {
        MapSample sample = new MapSample
        {
            Frame = Time.frameCount,
            ElapsedSeconds = Time.unscaledTime - captureStart,
            ActiveChunks = mapManager.ActiveChunkCount,
            NextChunkIndex = mapManager.NextChunkIndex,
            Spawned = mapManager.TotalChunksSpawned,
            Recycled = mapManager.TotalChunksRecycled,
            PrepareCreations = mapManager.TotalPrepareCreations,
            FailedSpawns = mapManager.TotalFailedSpawnAttempts,
            PrepareRebuilds = mapManager.PrepareDemandRebuildCount,
            PreparePasses = mapManager.PreparePassCount,
            CollisionStateChanges = mapManager.CollisionCullingStateChanges,
            CoverageReady = mapManager.HasRequiredCoverage ? 1 : 0,
            NormalActive = poolManager != null ? poolManager.GetActiveCount(PoolId.NormalMap) : -1,
            SafeActive = poolManager != null ? poolManager.GetActiveCount(PoolId.SafeMap) : -1,
            NormalAvailable = poolManager != null ? poolManager.GetAvailableCount(PoolId.NormalMap) : -1,
            SafeAvailable = poolManager != null ? poolManager.GetAvailableCount(PoolId.SafeMap) : -1
        };
        mapSamples.Add(sample);
    }

    private static int ReadIntMember(MonoBehaviour target, string name)
    {
        // 부가 지표는 선택사항입니다. 없는 멤버는 -1로 저장합니다.
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo field = t.GetField(name, AnyInstanceField);
            if (field != null && field.FieldType == typeof(int) && !field.IsStatic)
            {
                try { return (int)field.GetValue(target); }
                catch { return -1; }
            }

            PropertyInfo property = t.GetProperty(name, AnyInstanceField);
            if (property != null && property.PropertyType == typeof(int) &&
                property.GetIndexParameters().Length == 0)
            {
                MethodInfo getter = property.GetGetMethod(true);
                if (getter == null || getter.IsStatic) return -1;
                try { return (int)property.GetValue(target, null); }
                catch { return -1; }
            }
        }
        return -1;
    }

    private void FinishCapture(bool completed)
    {
        if (state != CaptureState.Capturing) return;
        if (captureFeatureMetrics && lastFeatureSampleFrame != Time.frameCount)
            RecordFeatureSample();

        state = CaptureState.Finished;
        DisposeRecorders();
        RestoreProfilerSettings();

        try
        {
            SaveFrames(Path.Combine(outputDirectory, outputPrefix + "_frames.csv"));
            SaveSummary(Path.Combine(outputDirectory, outputPrefix + "_summary.csv"), completed);
            if (featureSamples.Count > 0)
                SaveFeatureMetrics(Path.Combine(outputDirectory, outputPrefix + "_feature_metrics.csv"));
            if (mapSamples.Count > 0)
                SaveMapMetrics(Path.Combine(outputDirectory, outputPrefix + "_map_metrics.csv"));

            Debug.Log("[AeroExperiment] 개별 측정 저장: " + outputDirectory, this);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private static string GetExperimentsRoot()
    {
        return Path.Combine(Application.persistentDataPath,
            "AeroBenchmarks", "Experiments");
    }

    private void SaveFrames(string path)
    {
        StringBuilder sb = new StringBuilder(Math.Max(1024, frames.Count * 90));
        // 기존 열 이름/순서를 유지하고 오른쪽에 CPU 세부 지표를 덧붙입니다.
        sb.AppendLine("frame,elapsed_seconds,frame_ms,main_thread_ms,gc_alloc_bytes,gc_used_bytes,system_used_bytes," +
            "render_thread_ms,physics_simulate_ms,physics_processing_ms,physics_sync_collider_ms");
        foreach (FrameSample sample in frames)
        {
            sb.Append(sample.Frame).Append(',')
              .Append(sample.ElapsedSeconds.ToString("F4", Culture)).Append(',')
              .Append(sample.FrameMilliseconds.ToString("F4", Culture)).Append(',')
              .Append(ToMilliseconds(sample.MainThreadNanoseconds)).Append(',')
              .Append(sample.GCAllocatedBytes).Append(',')
              .Append(sample.GCUsedBytes).Append(',')
              .Append(sample.SystemUsedBytes).Append(',')
              .Append(ToMilliseconds(sample.RenderThreadNanoseconds)).Append(',')
              .Append(ToMilliseconds(sample.PhysicsSimulateNanoseconds)).Append(',')
              .Append(ToMilliseconds(sample.PhysicsProcessingNanoseconds)).Append(',')
              .Append(ToMilliseconds(sample.PhysicsSyncColliderNanoseconds)).AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    private void SaveFeatureMetrics(string path)
    {
        StringBuilder sb = new StringBuilder(Math.Max(1024, featureSamples.Count * 110));
        sb.AppendLine("frame,elapsed_seconds,active_count,cloud_initial_target_count," +
                      "cloud_initial_placed_count,cloud_spawn_distance,cloud_spawn_range_limited," +
                      "cloud_rent_failures,pool_created,pool_active,pool_peak_active," +
                      "pool_available,pool_failed_rent,pool_runtime_creation");
        foreach (FeatureSample s in featureSamples)
        {
            sb.Append(s.Frame).Append(',')
              .Append(s.ElapsedSeconds.ToString("F4", Culture)).Append(',')
              .Append(s.ActiveCount).Append(',')
              .Append(s.CloudInitialTargetCount).Append(',')
              .Append(s.CloudInitialPlacedCount).Append(',')
              .Append(s.CloudSpawnDistance.ToString("F3", Culture)).Append(',')
              .Append(s.CloudSpawnRangeLimited).Append(',')
              .Append(s.CloudRentFailures).Append(',')
              .Append(s.PoolCreated).Append(',')
              .Append(s.PoolActive).Append(',')
              .Append(s.PoolPeakActive).Append(',')
              .Append(s.PoolAvailable).Append(',')
              .Append(s.PoolFailedRent).Append(',')
              .Append(s.PoolRuntimeCreation).AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    private void SaveMapMetrics(string path)
    {
        StringBuilder sb = new StringBuilder(Math.Max(1024, mapSamples.Count * 130));
        sb.AppendLine("frame,elapsed_seconds,active_chunks,next_chunk_index," +
                      "total_spawned,total_recycled,prepare_creations,failed_spawns," +
                      "prepare_demand_rebuilds,prepare_passes,collision_state_changes," +
                      "coverage_ready,normal_pool_active,safe_pool_active," +
                      "normal_pool_available,safe_pool_available");

        foreach (MapSample s in mapSamples)
        {
            sb.Append(s.Frame).Append(',')
              .Append(s.ElapsedSeconds.ToString("F4", Culture)).Append(',')
              .Append(s.ActiveChunks).Append(',')
              .Append(s.NextChunkIndex).Append(',')
              .Append(s.Spawned).Append(',')
              .Append(s.Recycled).Append(',')
              .Append(s.PrepareCreations).Append(',')
              .Append(s.FailedSpawns).Append(',')
              .Append(s.PrepareRebuilds).Append(',')
              .Append(s.PreparePasses).Append(',')
              .Append(s.CollisionStateChanges).Append(',')
              .Append(s.CoverageReady).Append(',')
              .Append(s.NormalActive).Append(',')
              .Append(s.SafeActive).Append(',')
              .Append(s.NormalAvailable).Append(',')
              .Append(s.SafeAvailable).AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    private void SaveSummary(string path, bool completed)
    {
        double totalFrameMs = 0;
        double totalMainThreadMs = 0;
        double totalRenderThreadMs = 0;
        double totalPhysicsSimulateMs = 0;
        double totalPhysicsProcessingMs = 0;
        double totalPhysicsSyncColliderMs = 0;
        double totalGC = 0;
        int mainThreadValid = 0;
        int renderThreadValid = 0;
        int physicsSimulateValid = 0;
        int physicsProcessingValid = 0;
        int physicsSyncColliderValid = 0;
        int gcValid = 0;
        long maximumGC = -1;
        long lastGCUsed = -1;
        long lastSystemUsed = -1;
        int peakGCFrame = -1;

        foreach (FrameSample f in frames)
        {
            totalFrameMs += f.FrameMilliseconds;
            if (f.MainThreadNanoseconds >= 0)
            {
                totalMainThreadMs += f.MainThreadNanoseconds * 0.000001;
                mainThreadValid++;
            }
            if (f.RenderThreadNanoseconds >= 0)
            {
                totalRenderThreadMs += f.RenderThreadNanoseconds * 0.000001;
                renderThreadValid++;
            }
            if (f.PhysicsSimulateNanoseconds >= 0)
            {
                totalPhysicsSimulateMs += f.PhysicsSimulateNanoseconds * 0.000001;
                physicsSimulateValid++;
            }
            if (f.PhysicsProcessingNanoseconds >= 0)
            {
                totalPhysicsProcessingMs += f.PhysicsProcessingNanoseconds * 0.000001;
                physicsProcessingValid++;
            }
            if (f.PhysicsSyncColliderNanoseconds >= 0)
            {
                totalPhysicsSyncColliderMs += f.PhysicsSyncColliderNanoseconds * 0.000001;
                physicsSyncColliderValid++;
            }
            if (f.GCAllocatedBytes >= 0)
            {
                totalGC += f.GCAllocatedBytes;
                gcValid++;
                if (f.GCAllocatedBytes > maximumGC)
                {
                    maximumGC = f.GCAllocatedBytes;
                    peakGCFrame = f.Frame;
                }
            }
            if (f.GCUsedBytes >= 0) lastGCUsed = f.GCUsedBytes;
            if (f.SystemUsedBytes >= 0) lastSystemUsed = f.SystemUsedBytes;
        }

        StringBuilder sb = new StringBuilder(2048);
        sb.AppendLine("metric,value");
        AppendMetric(sb, "experiment", ExperimentFilePrefix);
        AppendMetric(sb, "type", experiment.ToString());
        AppendMetric(sb, "category", category.ToString());
        AppendMetric(sb, "optimization_enabled", AppliedOptimizationState ? "true" : "false");
        AppendMetric(sb, "completed", completed ? "true" : "false");
        AppendMetric(sb, "unity_version", Application.unityVersion);
        AppendMetric(sb, "platform", Application.platform.ToString());
        AppendMetric(sb, "frame_count", frames.Count.ToString(Culture));
        AppendMetric(sb, "valid_gc_samples", gcValid.ToString(Culture));
        AppendMetric(sb, "valid_main_thread_samples", mainThreadValid.ToString(Culture));
        AppendMetric(sb, "valid_render_thread_samples", renderThreadValid.ToString(Culture));
        AppendMetric(sb, "valid_physics_simulate_samples", physicsSimulateValid.ToString(Culture));
        AppendMetric(sb, "valid_physics_processing_samples", physicsProcessingValid.ToString(Culture));
        AppendMetric(sb, "valid_physics_sync_collider_samples", physicsSyncColliderValid.ToString(Culture));
        AppendMetric(sb, "average_frame_ms", Average(totalFrameMs, frames.Count));
        AppendMetric(sb, "average_main_thread_ms", Average(totalMainThreadMs, mainThreadValid));
        AppendMetric(sb, "average_render_thread_ms", Average(totalRenderThreadMs, renderThreadValid));
        AppendMetric(sb, "average_physics_simulate_ms", Average(totalPhysicsSimulateMs, physicsSimulateValid));
        AppendMetric(sb, "average_physics_processing_ms", Average(totalPhysicsProcessingMs, physicsProcessingValid));
        AppendMetric(sb, "average_physics_sync_collider_ms", Average(totalPhysicsSyncColliderMs, physicsSyncColliderValid));
        AppendMetric(sb, "average_gc_alloc_bytes_per_frame", Average(totalGC, gcValid));
        AppendMetric(sb, "total_gc_alloc_bytes_observed", totalGC.ToString("F0", Culture));
        AppendMetric(sb, "maximum_gc_alloc_bytes_per_frame",
            maximumGC < 0 ? "unavailable" : maximumGC.ToString(Culture));
        AppendMetric(sb, "peak_gc_sample_frame", peakGCFrame.ToString(Culture));
        AppendMetric(sb, "last_gc_used_bytes", lastGCUsed.ToString(Culture));
        AppendMetric(sb, "last_system_used_bytes", lastSystemUsed.ToString(Culture));
        AppendMetric(sb, "feature_sample_count", featureSamples.Count.ToString(Culture));

        if (featureSamples.Count > 0)
        {
            FeatureSample first = featureSamples[0];
            FeatureSample last = featureSamples[featureSamples.Count - 1];
            int peakActiveObserved = -1;
            foreach (FeatureSample s in featureSamples)
                if (s.ActiveCount > peakActiveObserved) peakActiveObserved = s.ActiveCount;

            AppendMetric(sb, "feature_active_start", first.ActiveCount.ToString(Culture));
            AppendMetric(sb, "feature_active_end", last.ActiveCount.ToString(Culture));
            AppendMetric(sb, "feature_peak_active_observed", peakActiveObserved.ToString(Culture));
            AppendMetric(sb, "feature_pool_created_end", last.PoolCreated.ToString(Culture));
            AppendMetric(sb, "feature_pool_peak_active_end", last.PoolPeakActive.ToString(Culture));
            AppendMetric(sb, "feature_pool_runtime_creation_delta",
                first.PoolRuntimeCreation < 0 || last.PoolRuntimeCreation < 0
                    ? "unavailable"
                    : (last.PoolRuntimeCreation - first.PoolRuntimeCreation).ToString(Culture));
            if (category == ExperimentCategory.Cloud)
            {
                AppendMetric(sb, "cloud_initial_target_count", last.CloudInitialTargetCount.ToString(Culture));
                AppendMetric(sb, "cloud_initial_placed_count", last.CloudInitialPlacedCount.ToString(Culture));
                AppendMetric(sb, "cloud_effective_spawn_distance", last.CloudSpawnDistance.ToString("F3", Culture));
                AppendMetric(sb, "cloud_spawn_range_limited", last.CloudSpawnRangeLimited.ToString(Culture));
                AppendMetric(sb, "cloud_rent_failures_delta",
                    first.CloudRentFailures < 0 || last.CloudRentFailures < 0
                        ? "unavailable"
                        : (last.CloudRentFailures - first.CloudRentFailures).ToString(Culture));
            }
        }
        if (category == ExperimentCategory.Map && mapSamples.Count > 0)
        {
            MapSample first = mapSamples[0];
            MapSample last = mapSamples[mapSamples.Count - 1];
            int uncoveredSamples = 0;
            int peakActiveChunks = 0;
            foreach (MapSample s in mapSamples)
            {
                if (s.CoverageReady == 0) uncoveredSamples++;
                if (s.ActiveChunks > peakActiveChunks) peakActiveChunks = s.ActiveChunks;
            }

            AppendMetric(sb, "map_sample_count", mapSamples.Count.ToString(Culture));
            AppendMetric(sb, "map_active_chunks_end", last.ActiveChunks.ToString(Culture));
            AppendMetric(sb, "map_active_chunks_peak_observed", peakActiveChunks.ToString(Culture));
            AppendMetric(sb, "map_spawned_delta", (last.Spawned - first.Spawned).ToString(Culture));
            AppendMetric(sb, "map_recycled_delta", (last.Recycled - first.Recycled).ToString(Culture));
            AppendMetric(sb, "map_prepare_creations_delta", (last.PrepareCreations - first.PrepareCreations).ToString(Culture));
            AppendMetric(sb, "map_failed_spawns_delta", (last.FailedSpawns - first.FailedSpawns).ToString(Culture));
            AppendMetric(sb, "map_prepare_rebuilds_delta", (last.PrepareRebuilds - first.PrepareRebuilds).ToString(Culture));
            AppendMetric(sb, "map_prepare_passes_delta", (last.PreparePasses - first.PreparePasses).ToString(Culture));
            AppendMetric(sb, "map_collision_state_changes_delta", (last.CollisionStateChanges - first.CollisionStateChanges).ToString(Culture));
            AppendMetric(sb, "map_uncovered_samples", uncoveredSamples.ToString(Culture));
            AppendMetric(sb, "map_normal_pool_active_end", last.NormalActive.ToString(Culture));
            AppendMetric(sb, "map_safe_pool_active_end", last.SafeActive.ToString(Culture));
        }
        AppendMetric(sb, "raw_profiler_requested", saveProfilerRaw ? "true" : "false");
        AppendMetric(sb, "allocation_callstacks_requested",
            saveProfilerRaw && captureAllocationCallstacks ? "true" : "false");
        AppendMetric(sb, "raw_profiler_file", saveProfilerRaw ? profilerRawPath : "none");
        AppendMetric(sb, "output_directory", outputDirectory);
        File.WriteAllText(path, sb.ToString(), Utf8NoBom);
    }

    private void SaveProfilerSettings()
    {
        previousProfilerEnabled = Profiler.enabled;
        previousBinaryLogEnabled = Profiler.enableBinaryLog;
        previousCallstacksEnabled = Profiler.enableAllocationCallstacks;
        previousLogFile = Profiler.logFile;
        previousProfilerMemory = Profiler.maxUsedMemory;
        profilerStateSaved = true;
    }

    private void RestoreProfilerSettings()
    {
        if (!profilerStateSaved) return;
        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = string.Empty; // 프로파일러 파일 flush/닫기
        Profiler.enableAllocationCallstacks = previousCallstacksEnabled;
        Profiler.maxUsedMemory = previousProfilerMemory;
        Profiler.logFile = previousLogFile ?? string.Empty;
        Profiler.enableBinaryLog = previousBinaryLogEnabled;
        Profiler.enabled = previousProfilerEnabled;
        profilerStateSaved = false;
    }

    private void DisposeRecorders()
    {
        gcAllocatedRecorder.Dispose();
        gcUsedRecorder.Dispose();
        systemUsedRecorder.Dispose();
        mainThreadRecorder.Dispose();
        renderThreadRecorder.Dispose();
        physicsSimulateRecorder.Dispose();
        physicsProcessingRecorder.Dispose();
        physicsSyncColliderRecorder.Dispose();
        gcAllocatedRecorder = default;
        gcUsedRecorder = default;
        systemUsedRecorder = default;
        mainThreadRecorder = default;
        renderThreadRecorder = default;
        physicsSimulateRecorder = default;
        physicsProcessingRecorder = default;
        physicsSyncColliderRecorder = default;
    }

    private void OnDisable()
    {
        if (state == CaptureState.Capturing) FinishCapture(false);
        else
        {
            DisposeRecorders();
            RestoreProfilerSettings();
        }
    }

    private static long ReadLastValue(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue : InvalidMetric;
    }

    private static string ToMilliseconds(long nanoseconds)
    {
        return nanoseconds < 0 ? "-1" : (nanoseconds * 0.000001).ToString("F4", Culture);
    }

    private static string Average(double total, int count)
    {
        return count > 0 ? (total / count).ToString("F3", Culture) : "unavailable";
    }

    private static void AppendMetric(StringBuilder sb, string name, string value)
    {
        sb.Append(CsvText(name)).Append(',').Append(CsvText(value)).AppendLine();
    }

    private static string CsvText(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.IndexOfAny(new[] { ',', '\"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string CleanFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Custom";
        string result = value.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars()) result = result.Replace(ch, '_');
        // 폴더 구조/실험 구분용 구분 문자도 제거합니다.
        return result.Replace('/', '_').Replace('\\', '_').Replace('_', '-');
    }
}
