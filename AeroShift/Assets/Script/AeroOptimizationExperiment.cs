using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
#endif

// ����ȭ ON/OFF�� �������� �ʰ�, Unity�� �����ϴ� Profiler ��ġ�� CSV�� �����մϴ�.
// ���� enum, Reflection, RAW ����, Batch ���� ����� �����ϴ�.
[DisallowMultipleComponent]
public sealed class AeroOptimizationExperiment : MonoBehaviour
{
    [Header("CSV ���� ����")]
    [Tooltip("AeroBenchmarks/Experiments �Ʒ��� ��� ����Դϴ�. ��: Map/ColliderCulling/ON/Run01")]
    [SerializeField] private string saveSubfolder = "ProfilerCaptures";

    [Header("���� �ð�")]
    [SerializeField] private bool startAutomatically;
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [SerializeField, Min(1f)] private float captureSeconds = 30f;

    [Header("���� ������ ���� ���")]
    [Tooltip("�� �ð� �̻��� �����Ӹ� Editor Profiler ȣ�� Ʈ���� �߰� �����մϴ�. �⺻ 40ms.")]
    [SerializeField, Min(20f)] private float slowFrameThresholdMs = 40f;

    [Header("���� ���� - ���� �������� AeroFlip / AeroShift ��")]
    [Tooltip("�⺻ OFF: �ֿ� CPU/GPU/Physics/GC/Render ī���͸� ������ ���� ����� ����ϴ�. ON�� ����� ��� ��ǥ�� ������� �ϹǷ� ���� �񱳿��� �ƴ� ���� Ž����Դϴ�.")]
    [SerializeField] private bool recordEveryAvailableMetric = false;
    [Tooltip("���ÿ� �д� Recorder ���� �����Դϴ�. ��õ ���� ��ǥ�� ���� ���� ��ü�� ������ �� �ֽ��ϴ�.")]
    [SerializeField, Range(64, 8192)] private int maxRecorders = 128;
    [Tooltip("Recorder ��� ���� �迭�� �ִ� �޸� ����. ���� ���� Editor �޸𸮸� ũ�� ����մϴ�.")]
    [SerializeField, Range(16, 2048)] private int maxMetricBufferMiB = 32;
    [Header("Capture overhead controls")]
    [Tooltip("Enable only for a separate call-tree investigation. Baseline capture does not turn on the global Profiler.")]
    [SerializeField] private bool enableEditorDiagnostics;
    [Tooltip("Preallocate for this FPS. The capture reports truncation if this capacity is exceeded.")]
    [SerializeField, Range(60, 2000)] private int capacityFramesPerSecond = 1000;
    [SerializeField, Min(0.1f)] private float settleAfterAllocationSeconds = 1f;
    [Header("CPU/GPU ������ Ÿ�̹�")]
    [Tooltip("Unity FrameTimingManager�� N�����Ӹ��� ȣ���մϴ�. ����� 4������ �̻� �ʰ� ������ �� �־� ���� CSV�� �����մϴ�.")]
    [SerializeField, Range(4, 120)] private int frameTimingSampleEveryNFrames = 8;

    private sealed class AvailableMetric
    {
        public ProfilerRecorderHandle Handle;
        public string Category;
        public string Name;
        public string Unit;
        public int Priority;
        public string Status;
    }

    private struct FrameTimingPoint
    {
        public int ReadFrame;
        public float ReadElapsed;
        public double CpuTotal;
        public double CpuMain;
        public double CpuRender;
        public double CpuPresentWait;
        public double Gpu;
        public uint SyncInterval;
    }

    private sealed class SceneCensus
    {
        public string Stage;
        public int GameObjects;
        public int ActiveGameObjects;
        public int Renderers;
        public int Colliders;
        public int MeshColliders;
        public int EnabledColliders;
        public int EnabledMeshColliders;
        public int Rigidbodies;
        public int ParticleSystems;
        public int Behaviours;
        public int CloudNames;
        public int RoadNames;
        public int MissileNames;
    }

    private SceneCensus initialCensus;
    private SceneCensus finalCensus;

    private sealed class Metric
    {
        public string Category;
        public string Name;
        public string Unit;
        public string DataType;
        public ProfilerRecorder Recorder;
    }

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding CsvEncoding = new UTF8Encoding(true);

    private readonly List<Metric> metrics = new List<Metric>(256);
    private readonly List<AvailableMetric> availableMetrics = new List<AvailableMetric>(2048);
    private readonly List<FrameTimingPoint> frameTimingPoints = new List<FrameTimingPoint>(1024);
    private readonly FrameTiming[] frameTimingScratch = new FrameTiming[1];
    private bool frameTimingSupported;
    private string frameTimingStatus = "not_started";
    private double[] readRecorderMs;
    private int[] validRecorderReads;
    private int[] gcGen0;
    private int[] gcGen1;
    private int[] gcGen2;
    private int[] capturedGameStates;
    private int[] capturedPlayerModes;
    private float[] capturedWorldSpeeds;
    private float[] capturedWorldZ;
    private WorldMovement contextWorld;
    private PlayerMode contextPlayerMode;
    private int stopwatchTicksPerSecondChecked;
    private int availableMetricCount;
    private int effectiveRecorderLimit;
    private int skippedByScope;
    private int skippedByLimit;
    private int failedRecorderCount;
    private readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>(512);
    private readonly HashSet<string> uniqueMetrics = new HashSet<string>(StringComparer.Ordinal);

    private bool warmingUp;
    private bool preparingCapture;
    private int preparationFrame;
    private float preparationStartedAt;
    private int rejectedMetricValues;
    private bool recording;
    private bool finished;
    private bool profilerStateSaved;
    private bool previousProfilerEnabled;
#if UNITY_EDITOR
    private bool previousEditorProfilerDriverEnabled;
#endif
    private float warmupStart;
    private int warmupStartFrame;
    private float recordStart;
    private string outputDirectory;
    private string outputPrefix;
    private string finishReason;

    private int frameCapacity;
    private int recordedFrames;
    private int[] frameNumbers;
    private int[] fixedUpdateCounts;
    private int pendingFixedUpdates;
    private float[] elapsedSeconds;
    private float[] frameMilliseconds;
    private double[,] metricValues;
    private int invalidRecorderCount;
#if UNITY_EDITOR
    // Editor �������Ϸ� �������� ���� Time.frameCount�� ������ �ε����Դϴ�.
    private int editorStartFrame = -1;
    private int editorTraceFrames;
    private string editorTraceStatus = "not_started";

    // ProfilerDriver�� Game Time.frameCount�� ������ ������ �ε����� ����մϴ�.
    // ��ü 30�ʸ� ������ 180�����Ӹ� �����ϸ� �ʹ� ���� ������ũ�� ��Ĩ�ϴ�.
    // �׷��� Editor ������Ʈ���� �Ϸ�� �������� �ð��� Ȯ���ϰ�, ���� �����Ӹ� ��� �����մϴ�.
    private readonly List<EditorSpikeFrame> editorSpikeFrames = new List<EditorSpikeFrame>(24);
    private int editorLastInspected = -1;
    private int editorInspectedFrameCount;
    private int editorMissedFrameCount;
    private int editorDroppedSpikeCount;
    private string editorSpikeStatus = "not_started";
    private const int MaxEditorSpikeFrames = 48;
    private const int MaxEditorFramesPerTick = 8;
    private const int MaxEditorThreads = 64;
#endif

    // ���� ���� �и��� ���� �� �����ӿ� ����� ����� �������� ����մϴ�.
    // Physics.Simulate�� ProfilerRecorder.LastValue�� ���� �Ϸ� �������� �� �����Ƿ�
    // CSV�� frame_ms�� ������ �ϴ��� ����ȭ�� �������� �ʽ��ϴ�.
    private void Start()
    {
        if (startAutomatically)
            BeginWarmup();
    }

    private void Update()
    {
        if (warmingUp && Time.realtimeSinceStartup - warmupStart >= warmupSeconds)
        {
            warmingUp = false;
            BeginRecording();
        }

        if (preparingCapture && Time.frameCount >= preparationFrame + 3 &&
            Time.realtimeSinceStartup - preparationStartedAt >= settleAfterAllocationSeconds)
        {
            // ��Ը� ���� ���� ������ �Ͼ �������� GC �񱳿� �������� �ʽ��ϴ�.
            preparingCapture = false;
#if UNITY_EDITOR
            if (enableEditorDiagnostics)
            {
                editorStartFrame = ProfilerDriver.lastFrameIndex + 1;
                editorLastInspected = ProfilerDriver.lastFrameIndex;
            }
#endif
            recordStart = Time.realtimeSinceStartup;
            recording = true;
            pendingFixedUpdates = 0;
#if UNITY_EDITOR
            if (enableEditorDiagnostics)
                StartEditorSpikeObserver();
#endif
        }

        if (recording && Time.realtimeSinceStartup - recordStart >= captureSeconds)
            StopAndSave("duration_reached");
    }

    private void FixedUpdate()
    {
        if (recording)
            pendingFixedUpdates++;
    }

    private void LateUpdate()
    {
        if (!recording)
            return;

        if (recordedFrames >= frameCapacity)
        {
            StopAndSave("frame_capacity_reached");
            return;
        }

        long readStartTicks = Stopwatch.GetTimestamp();
        int row = recordedFrames;
        frameNumbers[row] = Time.frameCount;
        fixedUpdateCounts[row] = pendingFixedUpdates;
        pendingFixedUpdates = 0;
        elapsedSeconds[row] = Time.realtimeSinceStartup - recordStart;
        frameMilliseconds[row] = Time.unscaledDeltaTime * 1000f;
        capturedGameStates[row] = GameManager.Instance != null ? (int)GameManager.Instance.CurrentState : -1;
        capturedPlayerModes[row] = contextPlayerMode != null ? (int)contextPlayerMode.CurrentMode : -1;
        capturedWorldSpeeds[row] = contextWorld != null ? contextWorld.CurrentSpeed : float.NaN;
        capturedWorldZ[row] = contextWorld != null ? contextWorld.transform.position.z : float.NaN;

        // LastValue�� Unity�� '���������� �Ϸ��� ������'�� ����Դϴ�.
        // ���� LateUpdate�� ��Ȯ�� ���� �������� ��Ŀ��� �����ϸ� �� �˴ϴ�.
        int validReads = 0;
        for (int i = 0; i < metrics.Count; i++)
        {
            ProfilerRecorder recorder = metrics[i].Recorder;
            bool valid = recorder.Valid && recorder.Count > 0;
            double value = valid ? recorder.LastValueAsDouble : double.NaN;
            // These selected counters/timings are non-negative. Some Unity
            // recorders expose an invalid signed sentinel instead of a sample.
            if (valid && (double.IsNaN(value) || double.IsInfinity(value) || value < 0 ||
                (metrics[i].Name == "GPU Frame Time" &&
                 (value == 0 || ConvertUnit(value, metrics[i].Unit) >= 1000))))
            {
                value = double.NaN;
                valid = false;
                rejectedMetricValues++;
            }
            metricValues[row, i] = value;
            if (valid) validReads++;
        }
        validRecorderReads[row] = validReads;
        gcGen0[row] = GC.CollectionCount(0);
        gcGen1[row] = GC.CollectionCount(1);
        gcGen2[row] = GC.CollectionCount(2);
        readRecorderMs[row] = (Stopwatch.GetTimestamp() - readStartTicks) * 1000.0 / Stopwatch.Frequency;

        // FrameTiming�� �Ϸ�� ���� �������� �񵿱� ����Դϴ�. ���� ���� �����Ӱ� �������� �ʽ��ϴ�.
        if (frameTimingSupported && frameTimingSampleEveryNFrames > 0 &&
            (recordedFrames % frameTimingSampleEveryNFrames) == 0)
        {
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                uint fetched = FrameTimingManager.GetLatestTimings(1, frameTimingScratch);
                if (fetched > 0)
                {
                    FrameTiming timing = frameTimingScratch[0];
                    frameTimingPoints.Add(new FrameTimingPoint
                    {
                        ReadFrame = Time.frameCount,
                        ReadElapsed = elapsedSeconds[row],
                        CpuTotal = timing.cpuFrameTime,
                        CpuMain = timing.cpuMainThreadFrameTime,
                        CpuRender = timing.cpuRenderThreadFrameTime,
                        CpuPresentWait = timing.cpuMainThreadPresentWaitTime,
                        Gpu = timing.gpuFrameTime > 0 && timing.gpuFrameTime < 1000 ? timing.gpuFrameTime : double.NaN,
                        SyncInterval = timing.syncInterval
                    });
                }
            }
            catch (Exception ex)
            {
                frameTimingSupported = false;
                frameTimingStatus = "runtime_error_" + ex.GetType().Name;
            }
        }
        recordedFrames++;
    }

    [ContextMenu("Start Capture")]
    public void StartCaptureNow()
    {
        if (!Application.isPlaying || recording || preparingCapture || warmingUp)
            return;

        finished = false;
        BeginWarmup();
    }

    [ContextMenu("Stop and Save Capture")]
    public void StopCapture()
    {
        if (recording)
            StopAndSave("manual_stop");
        else if (warmingUp || preparingCapture)
        {
            warmingUp = false;
            preparingCapture = false;
            DisposeRecorders();
            ReleaseCaptureBuffers();
            RestoreProfilerState();
#if UNITY_EDITOR
            StopEditorSpikeObserver();
#endif
        }
    }

    private void BeginWarmup()
    {
        if (recording)
            return;

        try
        {
            previousProfilerEnabled = Profiler.enabled;
            // ���־� ���� Profiler�� Ȱ��ȭ�� ���� ��Ŀ�� ��ϵ� ��ȸ�� �ݴϴ�.
            if (enableEditorDiagnostics && !profilerStateSaved)
            {
                previousProfilerEnabled = Profiler.enabled;
#if UNITY_EDITOR
                previousEditorProfilerDriverEnabled = ProfilerDriver.enabled;
#endif
                profilerStateSaved = true;
            }

            if (enableEditorDiagnostics)
                Profiler.enabled = true;
#if UNITY_EDITOR
            // Profiler â�� Record ����ġ�� ���� �־ ���� �������� �����մϴ�.
            if (enableEditorDiagnostics)
                ProfilerDriver.enabled = true;
#endif
            warmupStart = Time.realtimeSinceStartup;
            warmupStartFrame = Time.frameCount;
            warmingUp = true;
            finished = false;
        }
        catch (Exception ex)
        {
            warmingUp = false;
            RestoreProfilerState();
            Debug.LogException(ex, this);
        }
    }

    private void BeginRecording()
    {
        if (!Application.isPlaying || recording || preparingCapture || finished)
            return;

        try
        {
            // ���־� ���� FPS�� ���� �� �ʿ��� ���۸� �� ���� �Ҵ��մϴ�.
            frameCapacity = Mathf.Clamp(
                Mathf.CeilToInt(captureSeconds * capacityFramesPerSecond) + 32,
                256, 120000);
            rejectedMetricValues = 0;

            outputDirectory = BuildOutputDirectory(saveSubfolder);
            Directory.CreateDirectory(outputDirectory);
            outputPrefix = SafeFileName(Application.productName) + "_Profiler_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Invariant);
            while (File.Exists(Path.Combine(outputDirectory, outputPrefix + "_summary.csv")))
                outputPrefix += "_1";

#if UNITY_EDITOR
            editorTraceFrames = 0;
            editorTraceStatus = enableEditorDiagnostics ? "waiting_for_capture_end" : "disabled_baseline_capture";
            editorSpikeFrames.Clear();
            editorLastInspected = -1;
            editorInspectedFrameCount = 0;
            editorMissedFrameCount = 0;
            editorDroppedSpikeCount = 0;
            editorSpikeStatus = enableEditorDiagnostics ? "waiting_for_capture_start" : "disabled_baseline_capture";
#endif
            DiscoverMetrics();
            initialCensus = TakeSceneCensus("before_capture_buffering");
            finalCensus = null;
            frameTimingPoints.Clear();
            frameTimingPoints.Capacity = Math.Max(frameTimingPoints.Capacity,
                frameCapacity / Math.Max(4, frameTimingSampleEveryNFrames) + 16);
            try
            {
                frameTimingSupported = FrameTimingManager.IsFeatureEnabled();
                frameTimingStatus = frameTimingSupported ? "supported_asynchronous_delayed" : "not_supported_on_platform_or_settings";
            }
            catch (Exception ex)
            {
                frameTimingSupported = false;
                frameTimingStatus = "not_available_" + ex.GetType().Name;
            }
            frameNumbers = new int[frameCapacity];
            fixedUpdateCounts = new int[frameCapacity];
            elapsedSeconds = new float[frameCapacity];
            frameMilliseconds = new float[frameCapacity];
            readRecorderMs = new double[frameCapacity];
            validRecorderReads = new int[frameCapacity];
            gcGen0 = new int[frameCapacity];
            gcGen1 = new int[frameCapacity];
            gcGen2 = new int[frameCapacity];
            capturedGameStates = new int[frameCapacity];
            capturedPlayerModes = new int[frameCapacity];
            capturedWorldSpeeds = new float[frameCapacity];
            capturedWorldZ = new float[frameCapacity];
            contextWorld = FindFirstObjectByType<WorldMovement>();
            contextPlayerMode = FindFirstObjectByType<PlayerMode>();
            metricValues = new double[frameCapacity, metrics.Count];
            recordedFrames = 0;
            preparationFrame = Time.frameCount;
            preparationStartedAt = Time.realtimeSinceStartup;
            preparingCapture = true;
            Debug.Log("[AeroProfiler] ���� ���� �غ� �Ϸ�. 3������ ���� CSV ��� ����: " + metrics.Count + "�� ��ǥ", this);
        }
        catch (Exception ex)
        {
            recording = false;
            preparingCapture = false;
            finished = true;
            DisposeRecorders();
            ReleaseCaptureBuffers();
            RestoreProfilerState();
#if UNITY_EDITOR
            StopEditorSpikeObserver();
#endif
            Debug.LogException(ex, this);
        }
    }

    private static int MetricPriority(string category, string name)
    {
        string c = category ?? "";
        string n = name ?? "";
        // Essential COUNTERS must precede thousands of optional timing markers.
        // Alphabetical category ordering previously filled the 512 slots before
        // Draw Calls, Batches, SetPass and Triangles were reached.
        if (CoreMetricNames.Contains(n)) return 0;
        if (n.StartsWith("AeroShift.", StringComparison.Ordinal) ||
            (c == "Scripts" && (n.IndexOf("CloudSpawner", StringComparison.Ordinal) >= 0 ||
             n.IndexOf("MapManager", StringComparison.Ordinal) >= 0 ||
             n.IndexOf("PoolManager", StringComparison.Ordinal) >= 0 ||
             n.IndexOf("WorldMovement", StringComparison.Ordinal) >= 0 ||
             n.IndexOf("Missile", StringComparison.Ordinal) >= 0 ||
             n.IndexOf("PlayerRewind", StringComparison.Ordinal) >= 0))) return 1;
        return 3;
    }

    private static readonly HashSet<string> CoreMetricNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Main Thread", "Render Thread", "PlayerLoop", "EditorLoop",
        "CPU Total Frame Time", "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time",
        "GC Allocated In Frame", "GC Allocation In Frame Count", "GC Used Memory", "GC Reserved Memory",
        "Total Used Memory", "Total Reserved Memory", "System Used Memory", "System Total Used Memory",
        "App Resident Memory", "App Committed Memory", "Profiler Used Memory", "Profiler Reserved Memory",
        "Texture Memory", "Mesh Memory", "Material Memory", "Gfx Used Memory", "Gfx Reserved Memory",
        "Physics Used Memory", "Physics Used Memory (2D)", "Audio Used Memory", "Audio Reserved Memory",
        "Game Object Count", "Object Count", "Scene Object Count", "Asset Count",
        "Texture Count", "Mesh Count", "Material Count", "AudioClip Count", "AudioClip Memory",
        "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count",
        "Shadow Casters Count", "Render Textures Count", "Render Textures Bytes", "Render Textures Changes Count",
        "GC.Alloc", "GC.Collect", "GarbageCollector.CollectIncremental",
        "Physics.Simulate", "PxScene.simulate", "Physics.SyncTransforms",
        "Physics.SyncColliderTransformBatchJob", "Physics.SyncRigidbodyTransformBatchJob",
        "Physics.Processing", "Physics.FetchResults", "Physics.ProcessReports",
        "Physics Queries", "Colliders Synced", "Rigidbodies Synced", "Static Colliders",
        "Dynamic Bodies", "Active Dynamic Bodies", "Active Kinematic Bodies", "Contacts",
        "Broadphase Adds", "Broadphase Removes", "Broadphase Adds/Removes", "Trigger Overlaps",
        "WaitForTargetFPS", "WaitForLastPresentation", "Gfx.WaitForRenderThread", "Gfx.WaitForPresentOnGfxThread",
        "GfxDeviceD3D12.WaitForGPU", "Semaphore.WaitForSignal"
    };

    private void DiscoverMetrics()
    {
        DisposeRecorders();
        handles.Clear();
        uniqueMetrics.Clear();
        availableMetrics.Clear();
        invalidRecorderCount = 0;
        skippedByScope = 0;
        skippedByLimit = 0;
        failedRecorderCount = 0;
        ProfilerRecorderHandle.GetAvailable(handles);
        for (int i = 0; i < handles.Count; i++)
        {
            ProfilerRecorderHandle handle = handles[i];
            if (!handle.Valid) continue;
            try
            {
                var d = ProfilerRecorderHandle.GetDescription(handle);
                string category = d.Category.ToString();
                string name = d.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!uniqueMetrics.Add(category + "\u001f" + name)) continue;
                availableMetrics.Add(new AvailableMetric
                {
                    Handle = handle,
                    Category = category,
                    Name = name,
                    Unit = d.UnitType.ToString(),
                    Priority = MetricPriority(category, name),
                    Status = "not_selected"
                });
            }
            catch (Exception) { invalidRecorderCount++; }
        }
        availableMetricCount = availableMetrics.Count;
        long maximumDoubles = (long)maxMetricBufferMiB * 1024L * 1024L / sizeof(double);
        int byMemory = (int)Math.Max(1L, maximumDoubles / Math.Max(1, frameCapacity));
        effectiveRecorderLimit = Math.Min(maxRecorders, byMemory);
        availableMetrics.Sort((a, b) =>
        {
            int pri = a.Priority.CompareTo(b.Priority);
            if (pri != 0) return pri;
            int cat = string.CompareOrdinal(a.Category, b.Category);
            return cat != 0 ? cat : string.CompareOrdinal(a.Name, b.Name);
        });

        for (int i = 0; i < availableMetrics.Count; i++)
        {
            AvailableMetric a = availableMetrics[i];
            if (!recordEveryAvailableMetric && a.Priority >= 3)
            {
                a.Status = "excluded_low_priority_in_baseline";
                skippedByScope++;
                continue;
            }
            if (metrics.Count >= effectiveRecorderLimit)
            {
                a.Status = "limit_reached";
                skippedByLimit++;
                continue;
            }
            try
            {
                // capacity=1: ����Ƽ�� ���ۿ� �Ϸ�� ������ ������ �� �ϳ��� ����.
                ProfilerRecorder recorder = new ProfilerRecorder(a.Handle, 1, ProfilerRecorderOptions.Default);
                if (!recorder.Valid)
                {
                    recorder.Dispose();
                    a.Status = "recorder_invalid";
                    failedRecorderCount++;
                    continue;
                }
                recorder.Start();
                a.Status = "recorded";
                metrics.Add(new Metric
                {
                    Category = a.Category,
                    Name = a.Name,
                    Unit = a.Unit,
                    DataType = recorder.DataType.ToString(),
                    Recorder = recorder
                });
            }
            catch (Exception ex)
            {
                a.Status = "recorder_exception_" + ex.GetType().Name;
                failedRecorderCount++;
            }
        }
    }

    private void StopAndSave(string reason)
    {
        if (!recording)
            return;

#if UNITY_EDITOR
        // ���� ���� ���� �̹� �Ϸ�� Editor �������� ���������� �н��ϴ�.
        if (enableEditorDiagnostics)
            CollectEditorSpikeFrames();
#endif
        recording = false;
        finished = true;
        finishReason = reason;
        // ������ ���� �� ������Ʈ ���� ���縦 �����մϴ�. ���� �����ӿ� ���Ե��� �ʽ��ϴ�.
        try { finalCensus = TakeSceneCensus("after_capture_finished"); }
        catch (Exception e) { Debug.LogWarning("[AeroProfiler] Scene Census ����: " + e.Message, this); }
#if UNITY_EDITOR
        StopEditorSpikeObserver();
#endif
        // Recorder�� ����/�����ϰ� �̸������� ��Ÿ�����ʹ� CSV �ۼ����� �����մϴ�.
        DisposeRecorders(true);
#if UNITY_EDITOR
        // ���� ���� ������ ���� ��, Unity Editor�� ���� �ִ� Profiler �����Ӹ�
        // �ؼ��մϴ�. �м��� ��� CPU/GC�� ���� ���ۿ� ���Ե��� �ʽ��ϴ�.
        try
        {
            if (enableEditorDiagnostics)
                WriteEditorSampleDiagnostics();
        }
        catch (Exception editorEx)
        {
            editorTraceStatus = "error: " + editorEx.GetType().Name + ": " + editorEx.Message;
            Debug.LogWarning("[AeroProfiler] Editor ���� CSV ���� ����: " + editorEx, this);
        }
#endif
        RestoreProfilerState();

        try
        {
            WriteFrames();
            WriteFrameTimingDiagnostics();
            WriteSceneCensus();
            WriteTopSlowFrames();
            WriteAvailableMetrics();
            WriteMetricCoverage();
            WriteMetricCatalog();
            WriteAllMetrics();
            WritePhysicsSpikeRecorders();
#if UNITY_EDITOR
            if (enableEditorDiagnostics)
                WriteEditorPhysicsSpikeDiagnostics();
#endif
            WriteSummary();
            Debug.Log("[AeroProfiler] CSV ���� �Ϸ�: " + outputDirectory, this);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
        finally
        {
            metrics.Clear();
            ReleaseCaptureBuffers();
        }
    }

    private void WriteFrames()
    {
        using (var writer = NewCsv("_frames.csv"))
        {
            writer.WriteLine("frame,elapsed_seconds,frame_ms,fps,fixed_update_calls_in_this_lateupdate,profiler_recorder_read_ms,valid_recorder_reads,gc_collections_gen0_cumulative,gc_collections_gen1_cumulative,gc_collections_gen2_cumulative,game_state,player_mode,world_speed,world_z");
            for (int row = 0; row < recordedFrames; row++)
            {
                double ms = frameMilliseconds[row];
                writer.Write(frameNumbers[row].ToString(Invariant));
                writer.Write(',');
                writer.Write(elapsedSeconds[row].ToString("F4", Invariant));
                writer.Write(',');
                writer.Write(ms.ToString("F4", Invariant));
                writer.Write(',');
                writer.Write(ms > 0 ? (1000.0 / ms).ToString("F3", Invariant) : "");
                writer.Write(',');
                writer.Write(fixedUpdateCounts[row].ToString(Invariant)); writer.Write(',');
                writer.Write(readRecorderMs[row].ToString("R", Invariant)); writer.Write(',');
                writer.Write(validRecorderReads[row].ToString(Invariant)); writer.Write(',');
                writer.Write(gcGen0[row].ToString(Invariant)); writer.Write(',');
                writer.Write(gcGen1[row].ToString(Invariant)); writer.Write(',');
                writer.Write(gcGen2[row].ToString(Invariant)); writer.Write(',');
                writer.Write(capturedGameStates[row].ToString(Invariant)); writer.Write(',');
                writer.Write(capturedPlayerModes[row].ToString(Invariant)); writer.Write(',');
                WriteOptionalNumber(writer, capturedWorldSpeeds[row]); writer.Write(',');
                WriteOptionalNumber(writer, capturedWorldZ[row]); writer.WriteLine();
            }
        }
    }

    private static SceneCensus TakeSceneCensus(string stage)
    {
        // FindObjectsByType�� �迭�� �Ҵ��մϴ�. ���־� �غ�/���� ���� �ÿ��� ȣ���մϴ�.
        var c = new SceneCensus { Stage = stage };
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        c.GameObjects = objects.Length;
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject go = objects[i];
            if (go.activeInHierarchy) c.ActiveGameObjects++;
            string n = go.name;
            if (n.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0) c.CloudNames++;
            if (n.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0) c.RoadNames++;
            if (n.IndexOf("missile", StringComparison.OrdinalIgnoreCase) >= 0) c.MissileNames++;
        }
        c.Renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        c.Colliders = colliders.Length;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            bool active = collider.enabled && collider.gameObject.activeInHierarchy;
            if (active) c.EnabledColliders++;
            if (collider is MeshCollider)
            {
                c.MeshColliders++;
                if (active) c.EnabledMeshColliders++;
            }
        }
        c.Rigidbodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        c.ParticleSystems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        c.Behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        return c;
    }

    private void WriteSceneCensus()
    {
        using (var w = NewCsv("_scene_census.csv"))
        {
            w.WriteLine("stage,gameobjects_total,gameobjects_active,renderers_total,colliders_total,mesh_colliders_total,rigidbodies_total,particle_systems_total,monobehaviours_total,cloud_name_matches,road_or_chunk_name_matches,missile_name_matches,colliders_active_enabled,mesh_colliders_active_enabled,note");
            WriteOneCensus(w, initialCensus);
            WriteOneCensus(w, finalCensus);
        }
    }

    private static void WriteOneCensus(StreamWriter w, SceneCensus c)
    {
        if (c == null) return;
        WriteEscaped(w, c.Stage); w.Write(',');
        int[] v = { c.GameObjects, c.ActiveGameObjects, c.Renderers, c.Colliders, c.MeshColliders,
                    c.Rigidbodies, c.ParticleSystems, c.Behaviours, c.CloudNames, c.RoadNames, c.MissileNames,
                    c.EnabledColliders, c.EnabledMeshColliders };
        for (int i = 0; i < v.Length; i++) { w.Write(v[i].ToString(Invariant)); w.Write(','); }
        WriteEscaped(w, "scene+loaded objects include inactive; name-based cloud/road/missile counts are heuristic; census runs outside measurement");
        w.WriteLine();
    }

    private void WriteFrameTimingDiagnostics()
    {
        using (var writer = NewCsv("_frame_timings.csv"))
        {
            writer.WriteLine("read_game_frame,read_elapsed_seconds,cpu_total_frame_ms,cpu_main_work_ms,cpu_render_work_ms,cpu_main_present_wait_ms,gpu_frame_ms,sync_interval,alignment_warning");
            foreach (FrameTimingPoint v in frameTimingPoints)
            {
                writer.Write(v.ReadFrame.ToString(Invariant)); writer.Write(',');
                writer.Write(v.ReadElapsed.ToString("F4", Invariant)); writer.Write(',');
                writer.Write(v.CpuTotal.ToString("R", Invariant)); writer.Write(',');
                writer.Write(v.CpuMain.ToString("R", Invariant)); writer.Write(',');
                writer.Write(v.CpuRender.ToString("R", Invariant)); writer.Write(',');
                writer.Write(v.CpuPresentWait.ToString("R", Invariant)); writer.Write(',');
                WriteOptionalNumber(writer, v.Gpu); writer.Write(',');
                writer.Write(v.SyncInterval.ToString(Invariant)); writer.Write(',');
                WriteEscaped(writer, "ASYNC: delayed completed timing, not the read_game_frame; GPU zero/implausible values are unavailable");
                writer.WriteLine();
            }
        }
    }

    private void WriteTopSlowFrames()
    {
        using (var writer = NewCsv("_slowest_frames.csv"))
        {
            writer.WriteLine("rank,game_frame,elapsed_seconds,frame_ms,fixed_update_calls,recorder_read_ms,gc_gen0_delta,gc_gen1_delta,gc_gen2_delta,player_loop_ms,main_thread_ms,physics_simulate_ms,wait_for_last_presentation_ms,draw_calls,batches_count,note");
            var indices = new List<int>(recordedFrames);
            for (int i = 0; i < recordedFrames; i++) indices.Add(i);
            indices.Sort((a, b) => frameMilliseconds[b].CompareTo(frameMilliseconds[a]));
            int n = Math.Min(indices.Count, 100);
            int playerLoop = FindMetric("PlayerLoop");
            int mainThread = FindMetric("Main Thread");
            int physics = FindMetric("Physics.Simulate");
            int wait = FindMetric("WaitForLastPresentation");
            int draws = FindMetric("Draw Calls Count");
            int batches = FindMetric("Batches Count");
            for (int rank = 0; rank < n; rank++)
            {
                int row = indices[rank];
                writer.Write((rank + 1).ToString(Invariant)); writer.Write(',');
                writer.Write(frameNumbers[row].ToString(Invariant)); writer.Write(',');
                writer.Write(elapsedSeconds[row].ToString("F4", Invariant)); writer.Write(',');
                writer.Write(frameMilliseconds[row].ToString("F4", Invariant)); writer.Write(',');
                writer.Write(fixedUpdateCounts[row].ToString(Invariant)); writer.Write(',');
                writer.Write(readRecorderMs[row].ToString("R", Invariant)); writer.Write(',');
                writer.Write((row > 0 ? gcGen0[row] - gcGen0[row - 1] : 0).ToString(Invariant)); writer.Write(',');
                writer.Write((row > 0 ? gcGen1[row] - gcGen1[row - 1] : 0).ToString(Invariant)); writer.Write(',');
                writer.Write((row > 0 ? gcGen2[row] - gcGen2[row - 1] : 0).ToString(Invariant)); writer.Write(',');
                int[] col = { playerLoop, mainThread, physics, wait, draws, batches };
                for (int j = 0; j < col.Length; j++)
                {
                    if (j != 0) writer.Write(',');
                    WriteOptionalNumber(writer, MetricMsOrNaN(row, col[j]));
                }
                writer.Write(',');
                WriteEscaped(writer, "profiler_recorder values can lag Time.unscaledDeltaTime; correlate with Connected Player trace frames");
                writer.WriteLine();
            }
        }
    }

    private void WriteAvailableMetrics()
    {
        using (var writer = NewCsv("_available_metrics.csv"))
        {
            writer.WriteLine("category,metric_name,unit,priority,status");
            foreach (AvailableMetric v in availableMetrics)
            {
                WriteEscaped(writer, v.Category); writer.Write(',');
                WriteEscaped(writer, v.Name); writer.Write(',');
                WriteEscaped(writer, v.Unit); writer.Write(',');
                writer.Write(v.Priority.ToString(Invariant)); writer.Write(',');
                WriteEscaped(writer, v.Status); writer.WriteLine();
            }
        }
    }

    private void WriteMetricCoverage()
    {
        // unavailable = Unity did not expose this marker to ProfilerRecorder here,
        // not proof that the corresponding gameplay system was never executed.
        string[] interesting = {
            "Main Thread", "PlayerLoop", "EditorLoop", "Render Thread", "CPU Total Frame Time",
            "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time",
            "WaitForTargetFPS", "WaitForLastPresentation", "Gfx.WaitForRenderThread",
            "Gfx.WaitForPresentOnGfxThread", "Physics.Simulate", "PxScene.simulate",
            "Physics.SyncColliderTransformBatchJob", "GC.Alloc", "GC.Collect",
            "GC Allocated In Frame", "Total Used Memory", "GC Used Memory",
            "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count",
            "Vertices Count", "CloudSpawner.Update", "AeroShift.MapStreaming.Update", "MapManager.Update",
            "WorldMovement.Update", "PoolManager.Update", "MissileSpawner.Update"
        };
        using (var writer = NewCsv("_critical_metric_coverage.csv"))
        {
            writer.WriteLine("wanted_marker,exact_name_found,recorded,available_with_other_category,interpretation");
            foreach (string name in interesting)
            {
                bool found = false;
                bool recorded = false;
                foreach (AvailableMetric v in availableMetrics)
                    if (v.Name == name || v.Name.Contains("::" + name + "()"))
                    {
                        found = true;
                        if (v.Status == "recorded") recorded = true;
                    }
                WriteEscaped(writer, name); writer.Write(',');
                writer.Write(found ? "true" : "false"); writer.Write(',');
                writer.Write(recorded ? "true" : "false"); writer.Write(',');
                writer.Write(found ? "true" : "false"); writer.Write(',');
                WriteEscaped(writer, found ? (recorded ? "counter active; values may lag one frame" : "available but not recorded; see available_metrics.csv") :
                    "NOT EXPOSED TO PROFILERRECORDER; not equivalent to zero; connect Unity Profiler for call tree");
                writer.WriteLine();
            }
        }
    }

    private void WriteMetricCatalog()
    {
        using (var writer = NewCsv("_metric_catalog.csv"))
        {
            writer.WriteLine("metric_id,category,metric_name,unit,converted_unit,data_type,valid_samples");
            for (int col = 0; col < metrics.Count; col++)
            {
                Metric metric = metrics[col];
                int valid = 0;
                for (int row = 0; row < recordedFrames; row++)
                    if (!double.IsNaN(metricValues[row, col])) valid++;

                writer.Write(col.ToString(Invariant)); writer.Write(',');
                WriteEscaped(writer, metric.Category); writer.Write(',');
                WriteEscaped(writer, metric.Name); writer.Write(',');
                WriteEscaped(writer, metric.Unit); writer.Write(',');
                WriteEscaped(writer, GetConvertedUnit(metric.Unit)); writer.Write(',');
                WriteEscaped(writer, metric.DataType); writer.Write(',');
                writer.WriteLine(valid.ToString(Invariant));
            }
        }
    }

    private void WriteAllMetrics()
    {
        // ��(����) ����: �� ���� �ϳ��� ��� �������Դϴ�.
        // ��� Recorder ���� ��ǥ�� �ڵ����� ���� ����ϴ�.
        using (var writer = NewCsv("_all_metrics.csv"))
        {
            writer.Write("frame,elapsed_seconds");
            for (int col = 0; col < metrics.Count; col++)
            {
                Metric metric = metrics[col];
                writer.Write(',');
                WriteEscaped(writer, metric.Category + "/" + metric.Name + " [" +
                    GetConvertedUnit(metric.Unit) + "]");
            }
            writer.WriteLine();

            for (int row = 0; row < recordedFrames; row++)
            {
                writer.Write(frameNumbers[row].ToString(Invariant)); writer.Write(',');
                writer.Write(elapsedSeconds[row].ToString("F4", Invariant));
                for (int col = 0; col < metrics.Count; col++)
                {
                    writer.Write(',');
                    double value = metricValues[row, col];
                    if (!double.IsNaN(value) && !double.IsInfinity(value))
                        writer.Write(ConvertUnit(value, metrics[col].Unit).ToString("R", Invariant));
                }
                writer.WriteLine();
            }
        }
    }


    // ����� ��� ���� ī���� ��� ���� �������� ���� ���� CSV�� �����մϴ�.
    // �� ����� Editor�� Development Build ���ʿ��� �����մϴ�.
    private int FindMetric(string metricName)
    {
        for (int i = 0; i < metrics.Count; i++)
            if (string.Equals(metrics[i].Name, metricName, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private double MetricMsOrNaN(int row, int metricIndex)
    {
        if (metricIndex < 0) return double.NaN;
        double value = metricValues[row, metricIndex];
        return double.IsNaN(value) ? double.NaN : ConvertUnit(value, metrics[metricIndex].Unit);
    }

    private static void WriteOptionalNumber(StreamWriter writer, double value)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value))
            writer.Write(value.ToString("R", Invariant));
    }

    private void WritePhysicsSpikeRecorders()
    {
        int sim = FindMetric("Physics.Simulate");
        int pxSim = FindMetric("PxScene.simulate");
        int fixedPhysics = FindMetric("FixedUpdate.PhysicsFixedUpdate");
        int fetch = FindMetric("Physics.FetchResults");
        int sync = FindMetric("Physics.SyncColliderTransformBatchJob");
        int activeDynamic = FindMetric("Active Dynamic Bodies");
        int staticColliders = FindMetric("Static Colliders");
        int overlaps = FindMetric("Overlaps");
        int synced = FindMetric("Colliders Synced");
        int narrow = FindMetric("Narrowphase Touches");
        int broad = FindMetric("Broadphase Adds/Removes");

        using (var writer = NewCsv("_physics_spike_frames.csv"))
        using (var details = NewCsv("_physics_spike_markers.csv"))
        {
            writer.WriteLine("game_frame,elapsed_seconds,frame_ms,fixed_update_calls,physics_simulate_ms,pxscene_simulate_ms,physics_fixedupdate_ms,physics_fetch_results_ms,physics_sync_collider_ms,active_dynamic_bodies,static_colliders,overlaps,colliders_synced,narrowphase_touches,broadphase_adds_removes,alignment_note");
            details.WriteLine("game_frame,elapsed_seconds,category,marker,unit,value,remark");
            for (int row = 0; row < recordedFrames; row++)
            {
                double physicsMs = MetricMsOrNaN(row, sim);
                if (frameMilliseconds[row] < slowFrameThresholdMs &&
                    (double.IsNaN(physicsMs) || physicsMs < slowFrameThresholdMs))
                    continue;

                writer.Write(frameNumbers[row].ToString(Invariant)); writer.Write(',');
                writer.Write(elapsedSeconds[row].ToString("F4", Invariant)); writer.Write(',');
                writer.Write(frameMilliseconds[row].ToString("F4", Invariant)); writer.Write(',');
                writer.Write(fixedUpdateCounts[row].ToString(Invariant)); writer.Write(',');
                int[] indices = { sim, pxSim, fixedPhysics, fetch, sync, activeDynamic, staticColliders, overlaps, synced, narrow, broad };
                for (int i = 0; i < indices.Length; i++)
                {
                    WriteOptionalNumber(writer, MetricMsOrNaN(row, indices[i]));
                    writer.Write(',');
                }
                WriteEscaped(writer, "ProfilerRecorder.LastValue can lag game frame; use editor spike frame index for exact hierarchy");
                writer.WriteLine();

                for (int col = 0; col < metrics.Count; col++)
                {
                    Metric m = metrics[col];
                    if (m.Category != "Physics" && m.Category != "Physics2D" &&
                        !(m.Category == "PlayerLoop" && m.Name.IndexOf("FixedUpdate", StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                    double value = MetricMsOrNaN(row, col);
                    if (double.IsNaN(value) || value <= 0) continue;
                    details.Write(frameNumbers[row].ToString(Invariant)); details.Write(',');
                    details.Write(elapsedSeconds[row].ToString("F4", Invariant)); details.Write(',');
                    WriteEscaped(details, m.Category); details.Write(',');
                    WriteEscaped(details, m.Name); details.Write(',');
                    WriteEscaped(details, GetConvertedUnit(m.Unit)); details.Write(',');
                    details.Write(value.ToString("R", Invariant)); details.Write(',');
                    WriteEscaped(details, "ProfilerRecorder completed-frame metric; overlapping inclusive markers");
                    details.WriteLine();
                }
            }
        }
    }

    private void WriteSummary()
    {
        using (var writer = NewCsv("_summary.csv"))
        {
            writer.WriteLine("section,category,metric_name,unit,valid_samples,average,minimum,maximum,details");
            WriteMetadata(writer, "unity_version", Application.unityVersion);
            WriteMetadata(writer, "debug_build", Debug.isDebugBuild.ToString().ToLowerInvariant());
            WriteMetadata(writer, "active_scene", SceneManager.GetActiveScene().name);
            WriteMetadata(writer, "screen_width", Screen.width.ToString(Invariant));
            WriteMetadata(writer, "screen_height", Screen.height.ToString(Invariant));
            WriteMetadata(writer, "fullscreen_mode", Screen.fullScreenMode.ToString());
            WriteMetadata(writer, "graphics_api", SystemInfo.graphicsDeviceType.ToString());
            WriteMetadata(writer, "gpu_name", SystemInfo.graphicsDeviceName);
            WriteMetadata(writer, "gpu_memory_mb", SystemInfo.graphicsMemorySize.ToString(Invariant));
            WriteMetadata(writer, "cpu_name", SystemInfo.processorType);
            WriteMetadata(writer, "cpu_logical_cores", SystemInfo.processorCount.ToString(Invariant));
            WriteMetadata(writer, "ram_mb", SystemInfo.systemMemorySize.ToString(Invariant));
            WriteMetadata(writer, "quality_level", QualitySettings.GetQualityLevel().ToString(Invariant));
            WriteMetadata(writer, "quality_name", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            WriteMetadata(writer, "antialiasing", QualitySettings.antiAliasing.ToString(Invariant));
            WriteMetadata(writer, "shadow_distance", QualitySettings.shadowDistance.ToString("R", Invariant));
            WriteMetadata(writer, "render_pipeline", GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "BuiltIn");
            WriteMetadata(writer, "time_scale", Time.timeScale.ToString("R", Invariant));
            WriteMetadata(writer, "record_every_available_metric", recordEveryAvailableMetric.ToString().ToLowerInvariant());
            WriteMetadata(writer, "capture_format_version", "2_baseline_core_counters");
            WriteMetadata(writer, "editor_diagnostics_enabled", enableEditorDiagnostics.ToString().ToLowerInvariant());
            WriteMetadata(writer, "global_profiler_was_enabled", previousProfilerEnabled.ToString().ToLowerInvariant());
            WriteMetadata(writer, "frame_capacity", frameCapacity.ToString(Invariant));
            WriteMetadata(writer, "metric_array_bytes", ((long)frameCapacity * metrics.Count * sizeof(double)).ToString(Invariant));
            WriteMetadata(writer, "rejected_invalid_metric_values", rejectedMetricValues.ToString(Invariant));
            WriteMetadata(writer, "buffer_settle_seconds", settleAfterAllocationSeconds.ToString("R", Invariant));
            WriteMetadata(writer, "baseline_scope", "Exact core counters plus game markers; missing metrics remain unavailable. Global Profiler is not enabled by baseline mode.");
            WriteMetadata(writer, "game_state_codes", "-1=unavailable; 0=Ready; 1=Playing; 2=GameOver");
            WriteMetadata(writer, "player_mode_codes", "-1=unavailable; 0=Mode3D; 1=Mode2D");
            WriteMetadata(writer, "game_context_alignment", "frames.csv context is read in the current LateUpdate; recorder columns can lag it");
            WriteMetadata(writer, "max_recorders", maxRecorders.ToString(Invariant));
            WriteMetadata(writer, "max_metric_buffer_mib", maxMetricBufferMiB.ToString(Invariant));
            WriteMetadata(writer, "effective_recorder_limit_after_memory_guard", effectiveRecorderLimit.ToString(Invariant));
            WriteMetadata(writer, "available_metric_count", availableMetricCount.ToString(Invariant));
            WriteMetadata(writer, "skipped_by_scope", skippedByScope.ToString(Invariant));
            WriteMetadata(writer, "skipped_by_limit", skippedByLimit.ToString(Invariant));
            WriteMetadata(writer, "recorder_open_failures", failedRecorderCount.ToString(Invariant));
            WriteMetadata(writer, "frametiming_status", frameTimingStatus);
            WriteMetadata(writer, "frametiming_samples", frameTimingPoints.Count.ToString(Invariant));
            WriteMetadata(writer, "frametiming_note", "FrameTiming results are delayed; compare distributions not row indices");
            WriteMetadata(writer, "profiler_read_cost", "frames.csv profiler_recorder_read_ms includes recorder reads and excludes the FrameTimingManager poll");
            WriteMetadata(writer, "product_name", Application.productName);
            WriteMetadata(writer, "is_editor", Application.isEditor.ToString().ToLowerInvariant());
            WriteMetadata(writer, "target_frame_rate", Application.targetFrameRate.ToString(Invariant));
            WriteMetadata(writer, "v_sync_count", QualitySettings.vSyncCount.ToString(Invariant));
#if UNITY_EDITOR
            WriteMetadata(writer, "editor_trace_status", editorTraceStatus);
            WriteMetadata(writer, "editor_spike_status", editorSpikeStatus);
            WriteMetadata(writer, "editor_spike_frames_saved", editorSpikeFrames.Count.ToString(Invariant));
            WriteMetadata(writer, "editor_spike_frames_inspected", editorInspectedFrameCount.ToString(Invariant));
            WriteMetadata(writer, "editor_spike_frames_missed", editorMissedFrameCount.ToString(Invariant));
            WriteMetadata(writer, "editor_spike_frames_over_limit", editorDroppedSpikeCount.ToString(Invariant));
            WriteMetadata(writer, "editor_trace_frames", editorTraceFrames.ToString(Invariant));
            WriteMetadata(writer, "editor_trace_note", "Editor Profiler retained frames only; separate indices from frame.csv");
#endif
            WriteMetadata(writer, "platform", Application.platform.ToString());
            WriteMetadata(writer, "output_directory", outputDirectory);
            WriteMetadata(writer, "finish_reason", finishReason);
            WriteMetadata(writer, "complete_duration", (finishReason == "duration_reached").ToString().ToLowerInvariant());
            WriteMetadata(writer, "recorded_frames", recordedFrames.ToString(Invariant));
            if (recordedFrames > 0)
            {
                double profilerReadTotal = 0;
                for (int ri = 0; ri < recordedFrames; ri++) profilerReadTotal += readRecorderMs[ri];
                WriteMetadata(writer, "mean_recorder_read_ms", (profilerReadTotal / recordedFrames).ToString("R", Invariant));
                WriteMetadata(writer, "gc_gen0_collections_delta", (gcGen0[recordedFrames - 1] - gcGen0[0]).ToString(Invariant));
                WriteMetadata(writer, "gc_gen1_collections_delta", (gcGen1[recordedFrames - 1] - gcGen1[0]).ToString(Invariant));
                WriteMetadata(writer, "gc_gen2_collections_delta", (gcGen2[recordedFrames - 1] - gcGen2[0]).ToString(Invariant));
            }
            WriteMetadata(writer, "discovered_metrics", metrics.Count.ToString(Invariant));
            WriteMetadata(writer, "unavailable_recorders", invalidRecorderCount.ToString(Invariant));
            WriteMetadata(writer, "samples_alignment", "Profiler LastValue is from the last completed frame; may lag frame_ms");
            WriteMetadata(writer, "capture_start_after_buffer_allocation_frames", "3");
            WriteMetadata(writer, "physics_spike_threshold_ms", slowFrameThresholdMs.ToString("F2", Invariant));
            WriteMetadata(writer, "fixed_delta_time_seconds", Time.fixedDeltaTime.ToString("R", Invariant));
            WriteMetadata(writer, "maximum_delta_time_seconds", Time.maximumDeltaTime.ToString("R", Invariant));
            WriteMetadata(writer, "physics_spike_note", "FixedUpdate calls count reflects callbacks before LateUpdate; ProfilerRecorder.LastValue may lag");
            WriteMetadata(writer, "physics_spike_interpretation", "More than one FixedUpdate may indicate catchup; Physics.Simulate inclusive CPU time can contain engine waits; no Collider identity is inferred");

            if (recordedFrames > 0)
            {
                double sum = 0, min = double.MaxValue, max = double.MinValue;
                for (int row = 0; row < recordedFrames; row++)
                {
                    double value = frameMilliseconds[row];
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                WriteStat(writer, "frame", "Time", "frame_ms", "Milliseconds",
                    recordedFrames, sum / recordedFrames, min, max);
            }

            for (int col = 0; col < metrics.Count; col++)
            {
                int valid = 0;
                double sum = 0, min = double.MaxValue, max = double.MinValue;
                for (int row = 0; row < recordedFrames; row++)
                {
                    double value = metricValues[row, col];
                    if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                    valid++;
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                if (valid == 0) continue;
                Metric metric = metrics[col];
                WriteStat(writer, "profiler", metric.Category, metric.Name,
                    GetConvertedUnit(metric.Unit), valid,
                    ConvertUnit(sum / valid, metric.Unit),
                    ConvertUnit(min, metric.Unit),
                    ConvertUnit(max, metric.Unit));
            }
        }
    }

    private static void WriteMetadata(StreamWriter writer, string name, string value)
    {
        writer.Write("metadata,,"); WriteEscaped(writer, name);
        writer.Write(",,,,,,"); WriteEscaped(writer, value);
        writer.WriteLine();
    }

    private static void WriteStat(StreamWriter writer, string section, string category,
        string name, string unit, int count, double average, double min, double max)
    {
        WriteEscaped(writer, section); writer.Write(',');
        WriteEscaped(writer, category); writer.Write(',');
        WriteEscaped(writer, name); writer.Write(',');
        WriteEscaped(writer, unit); writer.Write(',');
        writer.Write(count.ToString(Invariant)); writer.Write(',');
        writer.Write(average.ToString("R", Invariant)); writer.Write(',');
        writer.Write(min.ToString("R", Invariant)); writer.Write(',');
        writer.Write(max.ToString("R", Invariant)); writer.WriteLine(',');
    }

    private StreamWriter NewCsv(string suffix)
    {
        return new StreamWriter(Path.Combine(outputDirectory, outputPrefix + suffix), false, CsvEncoding);
    }

    private static double ConvertUnit(double value, string unit)
    {
        if (unit == "TimeNanoseconds") return value / 1000000.0;
        if (unit == "Bytes") return value / 1024.0;
        return value;
    }

    private static string GetConvertedUnit(string unit)
    {
        if (unit == "TimeNanoseconds") return "ms";
        if (unit == "Bytes") return "KiB";
        return unit;
    }

    private static void WriteEscaped(StreamWriter writer, string value)
    {
        value = value ?? string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            writer.Write(value);
            return;
        }
        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\""));
        writer.Write('"');
    }

    private static string SafeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "UnityGame";
        var result = new StringBuilder(input.Length);
        const string windowsInvalid = "<>:\"/\\|?*";
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            result.Append(char.IsControl(ch) || windowsInvalid.IndexOf(ch) >= 0 ||
                Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }
        return result.ToString();
    }

    private static string BuildOutputDirectory(string subfolder)
    {
        string directory = Path.Combine(Application.persistentDataPath, "AeroBenchmarks", "Experiments");
        string[] parts = (subfolder ?? string.Empty).Replace('\\', '/').Split('/');
        char[] invalid = Path.GetInvalidFileNameChars();
        const string invalidForWindows = "<>:\"/\\|?*";
        foreach (string part in parts)
        {
            string trim = part.Trim().TrimEnd('.');
            if (trim.Length == 0 || trim == "." || trim == "..") continue;
            char[] chars = trim.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsControl(chars[i]) || Array.IndexOf(invalid, chars[i]) >= 0 || invalidForWindows.IndexOf(chars[i]) >= 0)
                    chars[i] = '_';
            string clean = new string(chars);
            if (clean != "." && clean != ".." && clean.Length > 0)
                directory = Path.Combine(directory, clean);
        }
        return directory;
    }

    private void DisposeRecorders(bool preserveMetadata = false)
    {
        for (int i = 0; i < metrics.Count; i++)
        {
            ProfilerRecorder recorder = metrics[i].Recorder;
            recorder.Dispose();
            metrics[i].Recorder = default;
        }
        if (!preserveMetadata)
            metrics.Clear();
    }

    private void RestoreProfilerState()
    {
        if (!profilerStateSaved) return;
        Profiler.enabled = previousProfilerEnabled;
#if UNITY_EDITOR
        ProfilerDriver.enabled = previousEditorProfilerDriverEnabled;
#endif
        profilerStateSaved = false;
    }

    private void ReleaseCaptureBuffers()
    {
        // Dispose releases native recorders; these managed arrays need their
        // references released too. Do not force GC.Collect during gameplay.
        metricValues = null;
        frameNumbers = null;
        fixedUpdateCounts = null;
        elapsedSeconds = null;
        frameMilliseconds = null;
        readRecorderMs = null;
        validRecorderReads = null;
        gcGen0 = gcGen1 = gcGen2 = null;
        capturedGameStates = capturedPlayerModes = null;
        capturedWorldSpeeds = capturedWorldZ = null;
        contextWorld = null;
        contextPlayerMode = null;
        frameTimingPoints.Clear();
        frameTimingPoints.TrimExcess();
        availableMetrics.Clear();
        handles.Clear();
        uniqueMetrics.Clear();
#if UNITY_EDITOR
        editorSpikeFrames.Clear();
#endif
    }

    private void OnDisable()
    {
        if (recording)
            StopAndSave("component_disabled");
        else
        {
            warmingUp = false;
            preparingCapture = false;
            DisposeRecorders();
            ReleaseCaptureBuffers();
            RestoreProfilerState();
        }
#if UNITY_EDITOR
        StopEditorSpikeObserver();
#endif
    }

#if UNITY_EDITOR

    private sealed class EditorSpikeFrame
    {
        public int FrameIndex;
        public double FrameMs;
        public double PlayerLoopMs;
        public double EditorLoopMs;
        public double MainThreadMs;
        public double PhysicsSimulateMs;
        public int PhysicsSimulateCalls;
        public int FixedUpdateMarkerCalls;
        public int ThreadCount;
        public int TotalSamples;
        public readonly List<EditorSpikeSample> Samples = new List<EditorSpikeSample>(512);
    }

    private sealed class EditorSpikeSample
    {
        public string Thread;
        public string Name;
        public int ParentIndex;
        public double StartMs;
        public double InclusiveMs;
        public double ExclusiveMs;
        public bool PhysicsPath;
    }

    private struct EditorSpikeScope
    {
        public int EndExclusive;
        public int NodeIndex;
    }

    private void StartEditorSpikeObserver()
    {
        editorLastInspected = ProfilerDriver.lastFrameIndex;
        editorSpikeStatus = "observing_completed_profiler_frames";
        EditorApplication.update -= CollectEditorSpikeFrames;
        EditorApplication.update += CollectEditorSpikeFrames;
    }

    private void StopEditorSpikeObserver()
    {
        EditorApplication.update -= CollectEditorSpikeFrames;
        if (editorSpikeStatus == "observing_completed_profiler_frames")
            editorSpikeStatus = "completed";
    }

    // EditorApplication.update���� �Ϸ�� Profiler frameTime�� Ȯ���մϴ�.
    // ��� �������� ��ü ������ ��ȸ���� �ʰ�, ���� �����ӿ����� �� �м��մϴ�.
    private void CollectEditorSpikeFrames()
    {
        if (!recording) return;
        try
        {
            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first) return;
            if (editorLastInspected > last)
            {
                // Profiler â�� Clear ������ ������ �ε����� �ʱ�ȭ�� ���
                editorLastInspected = first - 1;
                editorSpikeStatus = "history_was_reset_during_capture";
            }
            int next = Math.Max(first, editorLastInspected + 1);
            int processed = 0;
            while (next <= last && processed < MaxEditorFramesPerTick)
            {
                bool valid;
                double frameMs;
                using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(next, 0))
                {
                    valid = view.valid;
                    frameMs = valid ? view.frameTimeMs : 0;
                }
                if (!valid)
                {
                    editorMissedFrameCount++;
                    editorLastInspected = next;
                    next++;
                    processed++;
                    continue;
                }
                editorInspectedFrameCount++;
                if (frameMs >= slowFrameThresholdMs)
                {
                    if (editorSpikeFrames.Count < MaxEditorSpikeFrames)
                        CaptureEditorSpikeFrame(next, frameMs);
                    else
                        editorDroppedSpikeCount++;
                }
                editorLastInspected = next;
                next++;
                processed++;
            }
            if (next <= last && last - next > 64)
            {
                // �������� �뷮 �������� �� ���� ��ü�� �ٽ� �� ������ũ�� ������ �ʵ��� ����.
                editorMissedFrameCount += last - next - 32;
                editorLastInspected = last - 33;
            }
        }
        catch (Exception ex)
        {
            editorSpikeStatus = "collection_error_" + ex.GetType().Name;
            StopEditorSpikeObserver();
            Debug.LogWarning("[AeroProfiler] ���� ������ �� ��� ����: " + ex.Message, this);
        }
    }

    private void CaptureEditorSpikeFrame(int frameIndex, double frameMs)
    {
        var spike = new EditorSpikeFrame { FrameIndex = frameIndex, FrameMs = frameMs };
        var scopes = new List<EditorSpikeScope>(80);
        for (int threadIndex = 0; threadIndex < MaxEditorThreads; threadIndex++)
        {
            using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (!view.valid) break;
                spike.ThreadCount++;
                string thread = view.threadGroupName + "/" + view.threadName;
                if (threadIndex == 0) spike.MainThreadMs = view.frameTimeMs;
                scopes.Clear();
                int count = view.sampleCount;
                spike.TotalSamples += count;
                for (int i = 0; i < count; i++)
                {
                    while (scopes.Count > 0 && i >= scopes[scopes.Count - 1].EndExclusive)
                        scopes.RemoveAt(scopes.Count - 1);
                    int parentIndex = scopes.Count == 0 ? -1 : scopes[scopes.Count - 1].NodeIndex;
                    string name = view.GetSampleName(i) ?? string.Empty;
                    double inclusive = view.GetSampleTimeMs(i);
                    var node = new EditorSpikeSample
                    {
                        Thread = thread,
                        Name = name,
                        ParentIndex = parentIndex,
                        StartMs = view.GetSampleStartTimeMs(i),
                        InclusiveMs = inclusive,
                        ExclusiveMs = inclusive,
                        PhysicsPath = IsPhysicsSample(name) ||
                            (parentIndex >= 0 && spike.Samples[parentIndex].PhysicsPath)
                    };
                    if (parentIndex >= 0)
                        spike.Samples[parentIndex].ExclusiveMs -= inclusive;
                    int globalIndex = spike.Samples.Count;
                    spike.Samples.Add(node);
                    if (name == "Physics.Simulate")
                    {
                        spike.PhysicsSimulateCalls++;
                        spike.PhysicsSimulateMs += inclusive;
                    }
                    if (name == "FixedUpdate.PhysicsFixedUpdate" || name == "PhysicsFixedUpdate")
                        spike.FixedUpdateMarkerCalls++;
                    if (name == "PlayerLoop") spike.PlayerLoopMs += inclusive;
                    if (name == "EditorLoop") spike.EditorLoopMs += inclusive;
                    int children = view.GetSampleChildrenCountRecursive(i);
                    if (children > 0)
                        scopes.Add(new EditorSpikeScope
                        {
                            NodeIndex = globalIndex,
                            EndExclusive = i + children + 1
                        });
                }
            }
        }
        editorSpikeFrames.Add(spike);
    }

    private static bool IsPhysicsSample(string name)
    {
        return name.StartsWith("Physics.", StringComparison.Ordinal) ||
               name.StartsWith("Px", StringComparison.Ordinal) ||
               name.StartsWith("ScScene.", StringComparison.Ordinal) ||
               name.StartsWith("NpScene.", StringComparison.Ordinal) ||
               name.StartsWith("BpSAP.", StringComparison.Ordinal) ||
               name.StartsWith("AABBManager", StringComparison.Ordinal) ||
               name.StartsWith("FixedUpdate.Physics", StringComparison.Ordinal) ||
               name.IndexOf("NarrowPhase", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Solver", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildSpikePath(EditorSpikeFrame frame, int sampleIndex)
    {
        var chain = new List<string>(12);
        int index = sampleIndex;
        while (index >= 0 && chain.Count < 16)
        {
            EditorSpikeSample sample = frame.Samples[index];
            chain.Add(sample.Name);
            index = sample.ParentIndex;
        }
        chain.Reverse();
        return string.Join(" > ", chain.ToArray());
    }

    private void WriteEditorPhysicsSpikeDiagnostics()
    {
        // game frame�� editor_frame�� ���� �ε���. ���� ���� ��ȣ��� ��Ī�ϸ� �� �˴ϴ�.
        using (var frames = NewCsv("_editor_spike_frames.csv"))
        using (var samples = NewCsv("_editor_spike_samples.csv"))
        {
            frames.WriteLine("editor_frame,frame_ms,main_thread_frame_ms,player_loop_inclusive_ms,editor_loop_inclusive_ms,physics_simulate_calls,physics_simulate_total_inclusive_ms,physics_fixedupdate_marker_calls,thread_count,total_samples,note");
            samples.WriteLine("editor_frame,thread,marker_path,marker,inclusive_ms,approx_exclusive_ms,start_time_ms,physics_subtree,note");
            for (int f = 0; f < editorSpikeFrames.Count; f++)
            {
                EditorSpikeFrame frame = editorSpikeFrames[f];
                frames.Write(frame.FrameIndex.ToString(Invariant)); frames.Write(',');
                frames.Write(frame.FrameMs.ToString("R", Invariant)); frames.Write(',');
                frames.Write(frame.MainThreadMs.ToString("R", Invariant)); frames.Write(',');
                frames.Write(frame.PlayerLoopMs.ToString("R", Invariant)); frames.Write(',');
                frames.Write(frame.EditorLoopMs.ToString("R", Invariant)); frames.Write(',');
                frames.Write(frame.PhysicsSimulateCalls.ToString(Invariant)); frames.Write(',');
                frames.Write(frame.PhysicsSimulateMs.ToString("R", Invariant)); frames.Write(',');
                frames.Write(frame.FixedUpdateMarkerCalls.ToString(Invariant)); frames.Write(',');
                frames.Write(frame.ThreadCount.ToString(Invariant)); frames.Write(',');
                frames.Write(frame.TotalSamples.ToString(Invariant)); frames.Write(',');
                WriteEscaped(frames, "Unity Editor Profiler frame index; no guaranteed match to game Time.frameCount");
                frames.WriteLine();

                // ���� �� ��� + ���� Ʈ���� �߿��� ���ø� ����մϴ�.
                // CPU inclusive �ð��� �θ�/�ڽ��� ��ġ�Ƿ� �ջ� ����.
                var selected = new List<int>(192);
                for (int i = 0; i < frame.Samples.Count; i++)
                {
                    EditorSpikeSample sample = frame.Samples[i];
                    if ((sample.PhysicsPath && (sample.InclusiveMs >= 0.05 || sample.ExclusiveMs >= 0.05)) ||
                        sample.ExclusiveMs >= 0.5 || sample.InclusiveMs >= slowFrameThresholdMs * 0.4)
                        selected.Add(i);
                }
                selected.Sort((a, b) =>
                {
                    // ���� �ڱ� �ڽ��� �ð��� ū ��Ŀ�� ���� ��� ���/���� ����� �����ϱ� ���մϴ�.
                    int result = frame.Samples[b].ExclusiveMs.CompareTo(frame.Samples[a].ExclusiveMs);
                    return result != 0 ? result : frame.Samples[b].InclusiveMs.CompareTo(frame.Samples[a].InclusiveMs);
                });
                int limit = Math.Min(selected.Count, 600);
                for (int i = 0; i < limit; i++)
                {
                    EditorSpikeSample sample = frame.Samples[selected[i]];
                    samples.Write(frame.FrameIndex.ToString(Invariant)); samples.Write(',');
                    WriteEscaped(samples, sample.Thread); samples.Write(',');
                    WriteEscaped(samples, BuildSpikePath(frame, selected[i])); samples.Write(',');
                    WriteEscaped(samples, sample.Name); samples.Write(',');
                    samples.Write(sample.InclusiveMs.ToString("R", Invariant)); samples.Write(',');
                    samples.Write(Math.Max(0, sample.ExclusiveMs).ToString("R", Invariant)); samples.Write(',');
                    samples.Write(sample.StartMs.ToString("R", Invariant)); samples.Write(',');
                    samples.Write(sample.PhysicsPath ? "true" : "false"); samples.Write(',');
                    WriteEscaped(samples, "inclusive overlaps parent/child; exclusive is estimated by subtracting direct child times");
                    samples.WriteLine();
                }
            }
        }
    }

    // GC.Alloc�� �޸� �Ҵ� ��ġ�� ũ�⸦ �����մϴ�. RawFrameDataView�� ����
    // �ֱ� Editor Profiler �������� ȣ�� ������ �Ҵ� ������ CSV�� �����մϴ�.
    // Player ���忡�� �� �ڵ� ��ü�� ���Ե��� �ʽ��ϴ�.
    private sealed class EditorGcSite
    {
        public string Thread;
        public string Parent;
        public string ExampleStack;
        public long Bytes;
        public long LargestBytes;
        public int Allocations;
        public int FirstFrame = int.MaxValue;
        public int LastFrame = -1;
    }

    private sealed class EditorCpuSite
    {
        public string Thread;
        public string Marker;
        public double TotalInclusiveMs;
        public double MaximumInclusiveMs;
        public int Calls;
    }

    private struct EditorSampleScope
    {
        public int EndExclusive;
        public string Name;
    }

    private void WriteEditorSampleDiagnostics()
    {
        int last = ProfilerDriver.lastFrameIndex;
        int first = ProfilerDriver.firstFrameIndex;
        if (last < 0 || first < 0 || last < first)
        {
            editorTraceStatus = "no_editor_profiler_frames_available";
            return;
        }

        // Editor Profiler�� ������ ��ȯ �����丮�̹Ƿ� ���� 30�ʸ� �������� �ʽ��ϴ�.
        // ���� ������ ������ ���� �ʵ��� ���� �� �ֱ� �ִ� 180�����Ӹ� �ؼ��մϴ�.
        const int MaxEditorFrames = 180;
        int start = Math.Max(Math.Max(first, editorStartFrame), last - MaxEditorFrames + 1);
        if (start > last)
        {
            editorTraceStatus = "no_editor_frames_in_capture_window";
            return;
        }

        var gcSites = new Dictionary<string, EditorGcSite>(StringComparer.Ordinal);
        var cpuSites = new Dictionary<string, EditorCpuSite>(StringComparer.Ordinal);
        var scopes = new List<EditorSampleScope>(64);
        var callstack = new List<ulong>(32);
        var exampleAttempted = new HashSet<string>(StringComparer.Ordinal);
        int gcSampleCount = 0;
        int callstackExamples = 0;
        const int MaxCallstackExamples = 200;
        int validFrameCount = 0;
        const int MaxThreadsPerFrame = 64;

        using (var perFrame = NewCsv("_editor_gc_frames.csv"))
        {
            perFrame.WriteLine("editor_frame,thread,allocated_bytes,gc_alloc_samples");
            for (int frame = start; frame <= last; frame++)
            {
                bool frameFound = false;
                for (int threadIdx = 0; threadIdx < MaxThreadsPerFrame; threadIdx++)
                {
                    using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frame, threadIdx))
                    {
                        if (!view.valid)
                            break;
                        frameFound = true;
                        string thread = view.threadGroupName + "/" + view.threadName;
                        int gcId = view.GetMarkerId("GC.Alloc");
                        int samples = view.sampleCount;
                        scopes.Clear();
                        long frameGcBytes = 0;
                        int frameGcCount = 0;

                        for (int i = 0; i < samples; i++)
                        {
                            // Unity raw samples are in depth-first order. Each scope's
                            // child samples immediately follow its parent sample.
                            while (scopes.Count > 0 && i >= scopes[scopes.Count - 1].EndExclusive)
                                scopes.RemoveAt(scopes.Count - 1);

                            string parent = scopes.Count > 0
                                ? scopes[scopes.Count - 1].Name : "(thread root)";
                            int markerId = view.GetSampleMarkerId(i);
                            bool isGc = markerId == gcId && gcId != FrameDataView.invalidMarkerId;

                            if (isGc)
                            {
                                long bytes = view.GetSampleMetadataCount(i) > 0
                                    ? view.GetSampleMetadataAsLong(i, 0) : 0;
                                if (bytes > 0)
                                {
                                    frameGcCount++;
                                    frameGcBytes += bytes;
                                    gcSampleCount++;
                                    string key = thread + "\u001f" + parent;
                                    if (!gcSites.TryGetValue(key, out EditorGcSite site))
                                    {
                                        site = new EditorGcSite { Thread = thread, Parent = parent };
                                        gcSites.Add(key, site);
                                    }
                                    site.Bytes += bytes;
                                    site.Allocations++;
                                    if (bytes > site.LargestBytes) site.LargestBytes = bytes;
                                    if (frame < site.FirstFrame) site.FirstFrame = frame;
                                    if (frame > site.LastFrame) site.LastFrame = frame;

                                    // Full GC callstacks exist only when the Unity Profiler
                                    // window's Call Stacks > GC.Alloc option was enabled.
                                    // To avoid huge analysis cost, resolve an example per
                                    // parent marker rather than every allocation sample.
                                    if (callstackExamples < MaxCallstackExamples &&
                                        !exampleAttempted.Contains(key))
                                    {
                                        exampleAttempted.Add(key);
                                        callstackExamples++;
                                        callstack.Clear();
                                        view.GetSampleCallstack(i, callstack);
                                        if (callstack.Count > 0)
                                        {
                                            var resolved = new StringBuilder(128);
                                            int limit = Math.Min(callstack.Count, 8);
                                            for (int depth = 0; depth < limit; depth++)
                                            {
                                                FrameDataView.MethodInfo method =
                                                    view.ResolveMethodInfo(callstack[depth]);
                                                if (string.IsNullOrEmpty(method.methodName)) continue;
                                                if (resolved.Length > 0) resolved.Append(" <- ");
                                                resolved.Append(method.methodName);
                                                if (!string.IsNullOrEmpty(method.sourceFileName))
                                                {
                                                    resolved.Append(" (");
                                                    resolved.Append(method.sourceFileName);
                                                    resolved.Append(':');
                                                    resolved.Append(method.sourceFileLine);
                                                    resolved.Append(')');
                                                }
                                            }
                                            site.ExampleStack = resolved.ToString();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string name = view.GetSampleName(i);
                                double duration = view.GetSampleTimeMs(i);
                                if (duration > 0 && !string.IsNullOrEmpty(name))
                                {
                                    string key = thread + "\u001f" + name;
                                    if (!cpuSites.TryGetValue(key, out EditorCpuSite cpu))
                                    {
                                        cpu = new EditorCpuSite { Thread = thread, Marker = name };
                                        cpuSites.Add(key, cpu);
                                    }
                                    cpu.TotalInclusiveMs += duration;
                                    cpu.Calls++;
                                    if (duration > cpu.MaximumInclusiveMs) cpu.MaximumInclusiveMs = duration;
                                }
                            }

                            int children = view.GetSampleChildrenCountRecursive(i);
                            if (children > 0)
                            {
                                scopes.Add(new EditorSampleScope
                                {
                                    Name = view.GetSampleName(i),
                                    EndExclusive = i + children + 1
                                });
                            }
                        }

                        if (frameGcCount != 0)
                        {
                            perFrame.Write(frame.ToString(Invariant)); perFrame.Write(',');
                            WriteEscaped(perFrame, thread); perFrame.Write(',');
                            perFrame.Write(frameGcBytes.ToString(Invariant)); perFrame.Write(',');
                            perFrame.WriteLine(frameGcCount.ToString(Invariant));
                        }
                    }
                }
                if (frameFound) validFrameCount++;
            }
        }

        editorTraceFrames = validFrameCount;
        editorTraceStatus = validFrameCount == 0 ? "no_valid_frames" :
            "ok_recent_frames_only_" + validFrameCount + "_gc_samples_" + gcSampleCount;

        var sortedGc = new List<EditorGcSite>(gcSites.Values);
        sortedGc.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        using (var writer = NewCsv("_editor_gc_sources.csv"))
        {
            writer.WriteLine("thread,parent_profiler_sample,allocation_count,total_bytes,largest_bytes,first_editor_frame,last_editor_frame,example_callstack");
            foreach (EditorGcSite site in sortedGc)
            {
                WriteEscaped(writer, site.Thread); writer.Write(',');
                WriteEscaped(writer, site.Parent); writer.Write(',');
                writer.Write(site.Allocations.ToString(Invariant)); writer.Write(',');
                writer.Write(site.Bytes.ToString(Invariant)); writer.Write(',');
                writer.Write(site.LargestBytes.ToString(Invariant)); writer.Write(',');
                writer.Write(site.FirstFrame.ToString(Invariant)); writer.Write(',');
                writer.Write(site.LastFrame.ToString(Invariant)); writer.Write(',');
                WriteEscaped(writer, site.ExampleStack); writer.WriteLine();
            }
        }

        // Inclusive sample times overlap for parent/child markers. The individual
        // rows must not be summed as if they were separate parts of frame time.
        var sortedCpu = new List<EditorCpuSite>(cpuSites.Values);
        sortedCpu.Sort((a, b) => b.TotalInclusiveMs.CompareTo(a.TotalInclusiveMs));
        using (var writer = NewCsv("_editor_cpu_hotspots.csv"))
        {
            writer.WriteLine("thread,profiler_sample,calls,total_inclusive_ms,average_inclusive_ms_per_call,max_inclusive_ms");
            foreach (EditorCpuSite cpu in sortedCpu)
            {
                WriteEscaped(writer, cpu.Thread); writer.Write(',');
                WriteEscaped(writer, cpu.Marker); writer.Write(',');
                writer.Write(cpu.Calls.ToString(Invariant)); writer.Write(',');
                writer.Write(cpu.TotalInclusiveMs.ToString("R", Invariant)); writer.Write(',');
                writer.Write((cpu.TotalInclusiveMs / cpu.Calls).ToString("R", Invariant)); writer.Write(',');
                writer.WriteLine(cpu.MaximumInclusiveMs.ToString("R", Invariant));
            }
        }
    }
#endif

}
