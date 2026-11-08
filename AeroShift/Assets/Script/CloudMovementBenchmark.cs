using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CloudMovementBenchmark : MonoBehaviour
{
    private enum BenchmarkState
    {
        Waiting,
        WarmingUp,
        Recording,
        Finished
    }

    private struct FrameSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public float FrameMilliseconds;
        public float MainThreadMilliseconds;
        public float GcAllocatedKilobytes;
        public float SystemUsedMegabytes;
        public float CloudGroupMoveMilliseconds;
        public float CloudIndividualMoveMilliseconds;
        public float CloudRecycleMilliseconds;
        public int ActiveClouds;
        public int PoolActiveClouds;
        public int PoolCreatedClouds;
    }

    private struct MetricSummary
    {
        public double Sum;
        public int Count;
        public float Maximum;

        public void Add(float value)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                return;

            Sum += value;
            Count++;
            Maximum = Mathf.Max(Maximum, value);
        }

        public string Average => Count == 0 ? string.Empty : Format(Sum / Count);
        public string Peak => Count == 0 ? string.Empty : Format(Maximum);
    }

    [Header("References")]
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private PoolManager poolManager;

    [Header("A/B Test")]
    [SerializeField] private string variantName = "Grouped";
    [SerializeField] private string scenarioName = "3D_Flight";
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [SerializeField, Min(1f)] private float measurementSeconds = 30f;
    [SerializeField, Min(100)] private int maxSampleFrames = 20000;
    [SerializeField] private bool requireInitialPlacementComplete = true;
    [SerializeField] private bool uncapFrameRate = true;

    private FrameSample[] samples;
    private BenchmarkState state;
    private int sampleCount;
    private float elapsed;
    private int baseRuntimeCreations;
    private int baseFailedRents;
    private int baseDestroyed;
    private int originalVSync;
    private int originalTargetFrameRate;
    private bool frameSettingsChanged;

    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder gcRecorder;
    private ProfilerRecorder memoryRecorder;
    private ProfilerRecorder groupedMoveRecorder;
    private ProfilerRecorder individualMoveRecorder;
    private ProfilerRecorder recycleRecorder;

    private void Awake()
    {
        if (cloudSpawner == null)
            cloudSpawner = FindFirstObjectByType<CloudSpawner>();

        if (poolManager == null)
            poolManager = FindFirstObjectByType<PoolManager>();

        samples = new FrameSample[Mathf.Max(100, maxSampleFrames)];
        state = BenchmarkState.Waiting;
    }

    private void OnEnable()
    {
        mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
        groupedMoveRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AeroShift.Cloud.GroupedMove", 1);
        individualMoveRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AeroShift.Cloud.IndividualMove", 1);
        recycleRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AeroShift.Cloud.RecycleExpired", 1);
    }

    private void Start()
    {
        if (cloudSpawner == null || poolManager == null)
        {
            Debug.LogError("CloudMovementBenchmark: CloudSpawner 또는 PoolManager 참조가 없습니다.", this);
            enabled = false;
            return;
        }

        if (!uncapFrameRate)
            return;

        originalVSync = QualitySettings.vSyncCount;
        originalTargetFrameRate = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        frameSettingsChanged = true;
    }

    private void Update()
    {
        if (state == BenchmarkState.Finished)
            return;

        GameManager gameManager = GameManager.Instance;
        bool playing = gameManager != null && gameManager.CurrentState == GameStates.Playing;
        bool ready = !requireInitialPlacementComplete || cloudSpawner.InitialPlacementComplete;

        if (state == BenchmarkState.Waiting)
        {
            if (playing && ready)
            {
                state = BenchmarkState.WarmingUp;
                elapsed = 0f;
            }

            return;
        }

        if (state == BenchmarkState.WarmingUp)
        {
            if (!playing || !ready)
            {
                state = BenchmarkState.Waiting;
                elapsed = 0f;
                return;
            }

            elapsed += Time.unscaledDeltaTime;

            if (elapsed >= warmupSeconds)
            {
                elapsed = 0f;
                sampleCount = 0;
                baseRuntimeCreations = poolManager.GetRuntimeCreationCount(PoolId.Cloud);
                baseFailedRents = poolManager.GetFailedRentCount(PoolId.Cloud);
                baseDestroyed = poolManager.GetDestroyedCount(PoolId.Cloud);
                state = BenchmarkState.Recording;
                Debug.Log("CloudMovementBenchmark: " + variantName + " 기록 시작", this);
            }

            return;
        }

        if (!playing)
        {
            Complete(false);
            return;
        }

        if (sampleCount >= samples.Length)
        {
            Complete(false);
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        elapsed += deltaTime;

        samples[sampleCount] = new FrameSample
        {
            Frame = Time.frameCount,
            ElapsedSeconds = elapsed,
            FrameMilliseconds = deltaTime * 1000f,
            MainThreadMilliseconds = ReadRecorder(mainThreadRecorder, 0.000001f),
            GcAllocatedKilobytes = ReadRecorder(gcRecorder, 1f / 1024f),
            SystemUsedMegabytes = ReadRecorder(memoryRecorder, 1f / 1048576f),
            CloudGroupMoveMilliseconds = ReadRecorder(groupedMoveRecorder, 0.000001f),
            CloudIndividualMoveMilliseconds = ReadRecorder(individualMoveRecorder, 0.000001f),
            CloudRecycleMilliseconds = ReadRecorder(recycleRecorder, 0.000001f),
            ActiveClouds = cloudSpawner.ActiveCloudCount,
            PoolActiveClouds = poolManager.GetActiveCount(PoolId.Cloud),
            PoolCreatedClouds = poolManager.GetCreatedCount(PoolId.Cloud)
        };

        sampleCount++;

        if (elapsed >= measurementSeconds)
            Complete(true);
    }

    private void Complete(bool completed)
    {
        if (state == BenchmarkState.Finished)
            return;

        state = BenchmarkState.Finished;

        if (sampleCount == 0)
        {
            Debug.LogWarning("CloudMovementBenchmark: 기록된 데이터가 없습니다.", this);
            return;
        }

        MetricSummary frame = new MetricSummary();
        MetricSummary mainThread = new MetricSummary();
        MetricSummary gc = new MetricSummary();
        MetricSummary memory = new MetricSummary();
        MetricSummary grouped = new MetricSummary();
        MetricSummary individual = new MetricSummary();
        MetricSummary recycle = new MetricSummary();
        MetricSummary active = new MetricSummary();
        MetricSummary created = new MetricSummary();
        float[] sortedFrames = new float[sampleCount];
        int mismatchedCount = 0;

        StringBuilder frameCsv = new StringBuilder(sampleCount * 120);
        frameCsv.AppendLine("frame,elapsed_s,frame_ms,main_thread_ms,gc_alloc_kb,system_used_mb,grouped_move_ms,individual_move_ms,recycle_ms,active_clouds,pool_active_clouds,pool_created_clouds");

        for (int i = 0; i < sampleCount; i++)
        {
            FrameSample sample = samples[i];
            sortedFrames[i] = sample.FrameMilliseconds;
            frame.Add(sample.FrameMilliseconds);
            mainThread.Add(sample.MainThreadMilliseconds);
            gc.Add(sample.GcAllocatedKilobytes);
            memory.Add(sample.SystemUsedMegabytes);
            grouped.Add(sample.CloudGroupMoveMilliseconds);
            individual.Add(sample.CloudIndividualMoveMilliseconds);
            recycle.Add(sample.CloudRecycleMilliseconds);
            active.Add(sample.ActiveClouds);
            created.Add(sample.PoolCreatedClouds);

            if (sample.ActiveClouds != sample.PoolActiveClouds)
                mismatchedCount++;

            frameCsv.Append(sample.Frame.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(sample.ElapsedSeconds)).Append(',')
                .Append(Format(sample.FrameMilliseconds)).Append(',')
                .Append(FormatOptional(sample.MainThreadMilliseconds)).Append(',')
                .Append(FormatOptional(sample.GcAllocatedKilobytes)).Append(',')
                .Append(FormatOptional(sample.SystemUsedMegabytes)).Append(',')
                .Append(FormatOptional(sample.CloudGroupMoveMilliseconds)).Append(',')
                .Append(FormatOptional(sample.CloudIndividualMoveMilliseconds)).Append(',')
                .Append(FormatOptional(sample.CloudRecycleMilliseconds)).Append(',')
                .Append(sample.ActiveClouds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.PoolActiveClouds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(sample.PoolCreatedClouds.ToString(CultureInfo.InvariantCulture));
        }

        Array.Sort(sortedFrames);
        float p95 = Percentile(sortedFrames, 0.95f);
        float p99 = Percentile(sortedFrames, 0.99f);
        int createdDuringRun = poolManager.GetRuntimeCreationCount(PoolId.Cloud) - baseRuntimeCreations;
        int failedDuringRun = poolManager.GetFailedRentCount(PoolId.Cloud) - baseFailedRents;
        int destroyedDuringRun = poolManager.GetDestroyedCount(PoolId.Cloud) - baseDestroyed;

        StringBuilder summary = new StringBuilder(1024);
        summary.AppendLine("variant,scenario,complete,samples,measured_s,unity_version,platform,in_editor,resolution,avg_frame_ms,p95_frame_ms,p99_frame_ms,avg_main_thread_ms,avg_gc_alloc_kb,avg_system_used_mb,peak_system_used_mb,avg_active_clouds,peak_active_clouds,avg_pool_created_clouds,runtime_creations,failed_rents,destroyed_instances,active_count_mismatch_frames,avg_grouped_move_ms,avg_individual_move_ms,avg_recycle_ms");
        summary.Append(Csv(variantName)).Append(',')
            .Append(Csv(scenarioName)).Append(',')
            .Append(completed ? "true" : "false").Append(',')
            .Append(sampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Format(elapsed)).Append(',')
            .Append(Csv(Application.unityVersion)).Append(',')
            .Append(Csv(Application.platform.ToString())).Append(',')
            .Append(Application.isEditor ? "true" : "false").Append(',')
            .Append(Csv(Screen.width + "x" + Screen.height)).Append(',')
            .Append(frame.Average).Append(',')
            .Append(Format(p95)).Append(',')
            .Append(Format(p99)).Append(',')
            .Append(mainThread.Average).Append(',')
            .Append(gc.Average).Append(',')
            .Append(memory.Average).Append(',')
            .Append(memory.Peak).Append(',')
            .Append(active.Average).Append(',')
            .Append(active.Peak).Append(',')
            .Append(created.Average).Append(',')
            .Append(createdDuringRun.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(failedDuringRun.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(destroyedDuringRun.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(mismatchedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(grouped.Average).Append(',')
            .Append(individual.Average).Append(',')
            .AppendLine(recycle.Average);

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "AeroBenchmarks", "CloudMovement");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string prefix = SafeName(scenarioName) + "_" + SafeName(variantName) + "_" + stamp;
            string summaryPath = Path.Combine(directory, prefix + "_summary.csv");
            string framesPath = Path.Combine(directory, prefix + "_frames.csv");

            File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(false));
            File.WriteAllText(framesPath, frameCsv.ToString(), new UTF8Encoding(false));
            Debug.Log("CloudMovementBenchmark 완료: " + summaryPath + " | " + framesPath, this);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private static float ReadRecorder(ProfilerRecorder recorder, float multiplier)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue * multiplier : -1f;
    }

    private static float Percentile(float[] values, float percentile)
    {
        int index = Mathf.Clamp(Mathf.CeilToInt(values.Length * percentile) - 1, 0, values.Length - 1);
        return values[index];
    }

    private static string Format(double value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string FormatOptional(float value)
    {
        return value < 0f ? string.Empty : Format(value);
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unnamed";

        StringBuilder name = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            bool unsafeCharacter = current == '/' || current == '\\';

            for (int j = 0; j < invalid.Length && !unsafeCharacter; j++)
                unsafeCharacter = current == invalid[j];

            name.Append(unsafeCharacter ? '_' : current);
        }

        return name.ToString();
    }

    private void OnDisable()
    {
        mainThreadRecorder.Dispose();
        gcRecorder.Dispose();
        memoryRecorder.Dispose();
        groupedMoveRecorder.Dispose();
        individualMoveRecorder.Dispose();
        recycleRecorder.Dispose();

        if (frameSettingsChanged)
        {
            QualitySettings.vSyncCount = originalVSync;
            Application.targetFrameRate = originalTargetFrameRate;
            frameSettingsChanged = false;
        }
    }
}
