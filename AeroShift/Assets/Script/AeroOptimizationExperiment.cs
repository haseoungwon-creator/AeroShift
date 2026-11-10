using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

// 최적화 ON/OFF를 변경하지 않고, Unity가 제공하는 Profiler 수치만 CSV로 저장합니다.
// 실험 enum, Reflection, RAW 파일, Batch 실행 기능은 없습니다.
[DisallowMultipleComponent]
public sealed class AeroOptimizationExperiment : MonoBehaviour
{
    [Header("CSV 저장 폴더")]
    [Tooltip("AeroBenchmarks/Experiments 아래의 상대 경로입니다. 예: Map/ColliderCulling/ON/Run01")]
    [SerializeField] private string saveSubfolder = "ProfilerCaptures";

    [Header("측정 시간")]
    [SerializeField] private bool startAutomatically = true;
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [SerializeField, Min(1f)] private float captureSeconds = 30f;

    [Header("느린 프레임 원인 기록")]
    [Tooltip("이 시간 이상인 프레임만 Editor Profiler 호출 트리를 추가 저장합니다. 기본 40ms.")]
    [SerializeField, Min(20f)] private float slowFrameThresholdMs = 40f;

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
    private readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>(512);
    private readonly HashSet<string> uniqueMetrics = new HashSet<string>(StringComparer.Ordinal);

    private bool warmingUp;
    private bool preparingCapture;
    private int preparationFrame;
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
    // Editor 프로파일러 프레임은 게임 Time.frameCount와 별개의 인덱스입니다.
    private int editorStartFrame = -1;
    private int editorTraceFrames;
    private string editorTraceStatus = "not_started";

    // ProfilerDriver는 Game Time.frameCount와 별개의 프레임 인덱스를 사용합니다.
    // 전체 30초를 마지막 180프레임만 추출하면 초반 물리 스파이크를 놓칩니다.
    // 그래서 Editor 업데이트에서 완료된 프레임의 시간만 확인하고, 느린 프레임만 즉시 보존합니다.
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

    // 빠른 원인 분리를 위해 전 프레임에 남기는 기록은 숫자형만 사용합니다.
    // Physics.Simulate의 ProfilerRecorder.LastValue는 이전 완료 프레임일 수 있으므로
    // CSV의 frame_ms와 완전한 일대일 동기화를 보장하지 않습니다.
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

        if (preparingCapture && Time.frameCount >= preparationFrame + 3)
        {
            // 대규모 측정 버퍼 생성이 일어난 프레임을 GC 비교에 포함하지 않습니다.
            preparingCapture = false;
#if UNITY_EDITOR
            editorStartFrame = ProfilerDriver.lastFrameIndex + 1;
            editorLastInspected = ProfilerDriver.lastFrameIndex;
#endif
            recordStart = Time.realtimeSinceStartup;
            recording = true;
            pendingFixedUpdates = 0;
#if UNITY_EDITOR
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

        int row = recordedFrames;
        frameNumbers[row] = Time.frameCount;
        fixedUpdateCounts[row] = pendingFixedUpdates;
        pendingFixedUpdates = 0;
        elapsedSeconds[row] = Time.realtimeSinceStartup - recordStart;
        frameMilliseconds[row] = Time.unscaledDeltaTime * 1000f;

        // LastValue는 Unity가 '마지막으로 완료한 프레임'의 결과입니다.
        // 현재 LateUpdate와 정확히 같은 프레임의 마커라고 가정하면 안 됩니다.
        for (int i = 0; i < metrics.Count; i++)
        {
            ProfilerRecorder recorder = metrics[i].Recorder;
            metricValues[row, i] = recorder.Valid && recorder.Count > 0
                ? recorder.LastValueAsDouble
                : double.NaN;
        }

        recordedFrames++;
    }

    [ContextMenu("CSV 측정 - 지금 시작")]
    public void StartCaptureNow()
    {
        if (!Application.isPlaying || recording || preparingCapture || warmingUp)
            return;

        finished = false;
        BeginWarmup();
    }

    [ContextMenu("CSV 측정 - 종료 및 저장")]
    public void StopCapture()
    {
        if (recording)
            StopAndSave("manual_stop");
        else if (warmingUp || preparingCapture)
        {
            warmingUp = false;
            preparingCapture = false;
            DisposeRecorders();
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
            // 워밍업 동안 Profiler를 활성화해 동적 마커가 등록될 기회를 줍니다.
            if (!profilerStateSaved)
            {
                previousProfilerEnabled = Profiler.enabled;
#if UNITY_EDITOR
                previousEditorProfilerDriverEnabled = ProfilerDriver.enabled;
#endif
                profilerStateSaved = true;
            }

            Profiler.enabled = true;
#if UNITY_EDITOR
            // Profiler 창의 Record 스위치가 꺼져 있어도 진단 프레임을 수집합니다.
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
            // 워밍업 관측 FPS로 측정 중 필요한 버퍼를 한 번만 할당합니다.
            float observedSeconds = Time.realtimeSinceStartup - warmupStart;
            float observedFps = observedSeconds > 0.5f
                ? (Time.frameCount - warmupStartFrame) / observedSeconds
                : 120f;
            observedFps = Mathf.Clamp(observedFps, 30f, 500f);
            frameCapacity = Mathf.Clamp(
                Mathf.CeilToInt(captureSeconds * observedFps * 1.5f) + 32,
                256, 30000);

            outputDirectory = BuildOutputDirectory(saveSubfolder);
            Directory.CreateDirectory(outputDirectory);
            outputPrefix = SafeFileName(Application.productName) + "_Profiler_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Invariant);
            while (File.Exists(Path.Combine(outputDirectory, outputPrefix + "_summary.csv")))
                outputPrefix += "_1";

#if UNITY_EDITOR
            editorTraceFrames = 0;
            editorTraceStatus = "waiting_for_capture_end";
            editorSpikeFrames.Clear();
            editorLastInspected = -1;
            editorInspectedFrameCount = 0;
            editorMissedFrameCount = 0;
            editorDroppedSpikeCount = 0;
            editorSpikeStatus = "waiting_for_capture_start";
#endif
            DiscoverMetrics();
            frameNumbers = new int[frameCapacity];
            fixedUpdateCounts = new int[frameCapacity];
            elapsedSeconds = new float[frameCapacity];
            frameMilliseconds = new float[frameCapacity];
            metricValues = new double[frameCapacity, metrics.Count];
            recordedFrames = 0;
            preparationFrame = Time.frameCount;
            preparingCapture = true;
            Debug.Log("[AeroProfiler] 측정 버퍼 준비 완료. 3프레임 이후 CSV 기록 시작: " + metrics.Count + "개 지표", this);
        }
        catch (Exception ex)
        {
            recording = false;
            preparingCapture = false;
            finished = true;
            DisposeRecorders();
            RestoreProfilerState();
#if UNITY_EDITOR
            StopEditorSpikeObserver();
#endif
            Debug.LogException(ex, this);
        }
    }

    private void DiscoverMetrics()
    {
        DisposeRecorders();
        handles.Clear();
        uniqueMetrics.Clear();
        invalidRecorderCount = 0;
        ProfilerRecorderHandle.GetAvailable(handles);

        for (int i = 0; i < handles.Count; i++)
        {
            ProfilerRecorderHandle handle = handles[i];
            if (!handle.Valid)
                continue;

            try
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                string category = description.Category.ToString();
                string name = description.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                string uniqueKey = category + "\u001f" + name;
                if (!uniqueMetrics.Add(uniqueKey))
                    continue;

                // capacity=1이면 Unity 네이티브 Recorder에 최근 값 1개만 유지합니다.
                // 프레임별 값은 스크립트가 미리 할당한 버퍼에 저장합니다.
                ProfilerRecorder recorder = new ProfilerRecorder(
                    handle, 1, ProfilerRecorderOptions.Default);
                if (!recorder.Valid)
                {
                    recorder.Dispose();
                    invalidRecorderCount++;
                    continue;
                }
                recorder.Start();
                metrics.Add(new Metric
                {
                    Category = category,
                    Name = name,
                    Unit = description.UnitType.ToString(),
                    DataType = recorder.DataType.ToString(),
                    Recorder = recorder
                });
            }
            catch (Exception)
            {
                // 일부 플랫폼/빌드에서는 노출된 마커를 Recorder로 열 수 없습니다.
                invalidRecorderCount++;
            }
        }
    }

    private void StopAndSave(string reason)
    {
        if (!recording)
            return;

#if UNITY_EDITOR
        // 수집 종료 직전 이미 완료된 Editor 프레임을 마지막으로 읽습니다.
        CollectEditorSpikeFrames();
#endif
        recording = false;
        finished = true;
        finishReason = reason;
#if UNITY_EDITOR
        StopEditorSpikeObserver();
#endif
        // Recorder만 정지/해제하고 이름·단위 메타데이터는 CSV 작성까지 보관합니다.
        DisposeRecorders(true);
#if UNITY_EDITOR
        // 실제 측정 구간이 끝난 뒤, Unity Editor에 남아 있는 Profiler 프레임만
        // 해석합니다. 분석에 드는 CPU/GC는 측정 버퍼에 포함되지 않습니다.
        try
        {
            WriteEditorSampleDiagnostics();
        }
        catch (Exception editorEx)
        {
            editorTraceStatus = "error: " + editorEx.GetType().Name + ": " + editorEx.Message;
            Debug.LogWarning("[AeroProfiler] Editor 진단 CSV 추출 실패: " + editorEx, this);
        }
#endif
        RestoreProfilerState();

        try
        {
            WriteFrames();
            WriteMetricCatalog();
            WriteAllMetrics();
            WritePhysicsSpikeRecorders();
#if UNITY_EDITOR
            WriteEditorPhysicsSpikeDiagnostics();
#endif
            WriteSummary();
            Debug.Log("[AeroProfiler] CSV 저장 완료: " + outputDirectory, this);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
        finally
        {
            metrics.Clear();
        }
    }

    private void WriteFrames()
    {
        using (var writer = NewCsv("_frames.csv"))
        {
            writer.WriteLine("frame,elapsed_seconds,frame_ms,fps,fixed_update_calls_in_this_lateupdate");
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
                writer.WriteLine(fixedUpdateCounts[row].ToString(Invariant));
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
        // 열(가로) 형식: 한 행이 하나의 기록 프레임입니다.
        // 모든 Recorder 지원 지표에 자동으로 열을 만듭니다.
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


    // 기록한 모든 물리 카운터 가운데 느린 프레임의 값만 별도 CSV로 정리합니다.
    // 이 기록은 Editor와 Development Build 양쪽에서 동작합니다.
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

    private void OnDisable()
    {
        if (recording)
            StopAndSave("component_disabled");
        else
        {
            warmingUp = false;
            preparingCapture = false;
            DisposeRecorders();
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

    // EditorApplication.update에서 완료된 Profiler frameTime만 확인합니다.
    // 모든 프레임의 전체 샘플을 순회하지 않고, 느린 프레임에서만 상세 분석합니다.
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
                // Profiler 창의 Clear 등으로 프레임 인덱스가 초기화된 경우
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
                // 프레임이 대량 누적됐을 때 진단 자체가 다시 긴 스파이크를 만들지 않도록 제한.
                editorMissedFrameCount += last - next - 32;
                editorLastInspected = last - 33;
            }
        }
        catch (Exception ex)
        {
            editorSpikeStatus = "collection_error_" + ex.GetType().Name;
            StopEditorSpikeObserver();
            Debug.LogWarning("[AeroProfiler] 느린 프레임 상세 기록 실패: " + ex.Message, this);
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
        // game frame와 editor_frame은 별도 인덱스. 서로 같은 번호라고 매칭하면 안 됩니다.
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

                // 실제 긴 경로 + 물리 트리의 중요한 샘플만 출력합니다.
                // CPU inclusive 시간은 부모/자식이 겹치므로 합산 금지.
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
                    // 먼저 자기 자신의 시간이 큰 마커를 보여 줘야 대기/실제 계산을 구분하기 편합니다.
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

    // GC.Alloc은 메모리 할당 위치와 크기를 포함합니다. RawFrameDataView를 통해
    // 최근 Editor Profiler 프레임의 호출 계층과 할당 샘플을 CSV로 추출합니다.
    // Player 빌드에는 이 코드 자체가 포함되지 않습니다.
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

        // Editor Profiler는 유한한 순환 히스토리이므로 이전 30초를 보장하지 않습니다.
        // 정상 측정에 영향을 주지 않도록 종료 후 최근 최대 180프레임만 해석합니다.
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
