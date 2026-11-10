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
    private float[] elapsedSeconds;
    private float[] frameMilliseconds;
    private double[,] metricValues;
    private int invalidRecorderCount;
#if UNITY_EDITOR
    // Editor 프로파일러 프레임은 게임 Time.frameCount와 별개의 인덱스입니다.
    private int editorStartFrame = -1;
    private int editorTraceFrames;
    private string editorTraceStatus = "not_started";
#endif

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
#endif
            recordStart = Time.realtimeSinceStartup;
            recording = true;
        }

        if (recording && Time.realtimeSinceStartup - recordStart >= captureSeconds)
            StopAndSave("duration_reached");
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
#endif
            DiscoverMetrics();
            frameNumbers = new int[frameCapacity];
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

        recording = false;
        finished = true;
        finishReason = reason;
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
            writer.WriteLine("frame,elapsed_seconds,frame_ms,fps");
            for (int row = 0; row < recordedFrames; row++)
            {
                double ms = frameMilliseconds[row];
                writer.Write(frameNumbers[row].ToString(Invariant));
                writer.Write(',');
                writer.Write(elapsedSeconds[row].ToString("F4", Invariant));
                writer.Write(',');
                writer.Write(ms.ToString("F4", Invariant));
                writer.Write(',');
                writer.WriteLine(ms > 0 ? (1000.0 / ms).ToString("F3", Invariant) : "");
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
    }

#if UNITY_EDITOR
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
