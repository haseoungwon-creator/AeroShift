using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

[DisallowMultipleComponent]
public sealed class GCAllocationCapture : MonoBehaviour
{
    [SerializeField] private bool startAutomaticallyWhenPlaying = true;
    [SerializeField] private float warmupSeconds = 5f;
    [SerializeField] private float captureSeconds = 10f;
    [SerializeField] private string scenarioName = "3D_Flight_GC";
    [SerializeField] private bool saveProfilerRaw = true;
    [SerializeField] private bool captureAllocationCallstacks = true;
    [SerializeField] private int profilerBufferMiB = 128;

    private enum CaptureState
    {
        Waiting,
        WarmingUp,
        Capturing,
        Finished
    }

    private struct FrameSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public float FrameMilliseconds;
        public long GCAllocatedBytes;
        public long GCUsedBytes;
        public long SystemUsedBytes;
    }

    private readonly List<FrameSample> frames = new List<FrameSample>(8192);
    private CaptureState state;
    private bool manualCapture;
    private float warmupStart;
    private float captureStart;
    private string outputDirectory;
    private string outputPrefix;
    private string profilerRawPath;

    private ProfilerRecorder gcAllocatedRecorder;
    private ProfilerRecorder gcUsedRecorder;
    private ProfilerRecorder systemUsedRecorder;

    private bool previousProfilerEnabled;
    private bool previousBinaryLogEnabled;
    private bool previousCallstacksEnabled;
    private string previousLogFile;
    private int previousProfilerMemory;
    private bool profilerStateSaved;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private void Update()
    {
        if (state == CaptureState.Waiting)
        {
            if (startAutomaticallyWhenPlaying && IsPlaying())
            {
                manualCapture = false;
                warmupStart = Time.unscaledTime;
                state = CaptureState.WarmingUp;
            }

            return;
        }

        if (state == CaptureState.WarmingUp)
        {
            if (!manualCapture && !IsPlaying())
            {
                state = CaptureState.Waiting;
                return;
            }

            if (Time.unscaledTime - warmupStart >= Mathf.Max(0f, warmupSeconds))
                BeginCapture();

            return;
        }

        if (state != CaptureState.Capturing)
            return;

        if (!manualCapture && !IsPlaying())
        {
            FinishCapture(false);
            return;
        }

        if (Time.unscaledTime - captureStart >= Mathf.Max(1f, captureSeconds))
            FinishCapture(true);
    }

    private void LateUpdate()
    {
        if (state != CaptureState.Capturing)
            return;

        frames.Add(new FrameSample
        {
            Frame = Time.frameCount,
            ElapsedSeconds = Time.unscaledTime - captureStart,
            FrameMilliseconds = Time.unscaledDeltaTime * 1000f,
            GCAllocatedBytes = ReadLastValue(gcAllocatedRecorder),
            GCUsedBytes = ReadLastValue(gcUsedRecorder),
            SystemUsedBytes = ReadLastValue(systemUsedRecorder)
        });
    }

    [ContextMenu("Start GC Capture Now")]
    public void StartCaptureNow()
    {
        if (!Application.isPlaying || state == CaptureState.Capturing)
            return;

        manualCapture = true;
        warmupStart = Time.unscaledTime;
        state = CaptureState.WarmingUp;
    }

    [ContextMenu("Stop GC Capture")]
    public void StopCapture()
    {
        if (state == CaptureState.Capturing)
            FinishCapture(false);
        else if (state == CaptureState.WarmingUp)
            state = CaptureState.Finished;
    }

    private void OnDisable()
    {
        if (state == CaptureState.Capturing)
            FinishCapture(false);
        else
            DisposeRecorders();
    }

    private static bool IsPlaying()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null && gameManager.CurrentState == GameStates.Playing;
    }

    private void BeginCapture()
    {
        frames.Clear();

        try
        {
            outputDirectory = Path.Combine(Application.persistentDataPath, "AeroBenchmarks", "GCInvestigation");
            Directory.CreateDirectory(outputDirectory);

            string cleanScenario = CleanFileName(scenarioName);
            outputPrefix = cleanScenario + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Culture);
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

            gcAllocatedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            gcUsedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory", 1);
            systemUsedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);

            captureStart = Time.unscaledTime;
            state = CaptureState.Capturing;
        }
        catch (Exception exception)
        {
            DisposeRecorders();
            RestoreProfilerSettings();
            state = CaptureState.Finished;
            Debug.LogException(exception, this);
        }
    }

    private void FinishCapture(bool completed)
    {
        if (state != CaptureState.Capturing)
            return;

        state = CaptureState.Finished;
        DisposeRecorders();
        RestoreProfilerSettings();

        try
        {
            string framesPath = Path.Combine(outputDirectory, outputPrefix + "_frames.csv");
            string summaryPath = Path.Combine(outputDirectory, outputPrefix + "_summary.csv");
            SaveFrames(framesPath);
            SaveSummary(summaryPath, completed);
            Debug.Log("GC Allocation Capture ¿Ï·á: " + summaryPath + " | " + framesPath + (saveProfilerRaw ? " | Profiler RAW: " + profilerRawPath : ""), this);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
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
        if (!profilerStateSaved)
            return;

        Profiler.enabled = false;
        Profiler.logFile = string.Empty;
        Profiler.enableBinaryLog = false;
        Profiler.enableAllocationCallstacks = previousCallstacksEnabled;
        Profiler.maxUsedMemory = previousProfilerMemory;

        if (!string.IsNullOrEmpty(previousLogFile))
            Profiler.logFile = previousLogFile;

        Profiler.enableBinaryLog = previousBinaryLogEnabled;
        Profiler.enabled = previousProfilerEnabled;
        profilerStateSaved = false;
    }

    private void DisposeRecorders()
    {
        gcAllocatedRecorder.Dispose();
        gcUsedRecorder.Dispose();
        systemUsedRecorder.Dispose();
    }

    private void SaveFrames(string path)
    {
        StringBuilder builder = new StringBuilder(Math.Max(1024, frames.Count * 64));
        builder.AppendLine("frame,elapsed_seconds,frame_ms,gc_alloc_bytes,gc_used_bytes,system_used_bytes");

        for (int i = 0; i < frames.Count; i++)
        {
            FrameSample sample = frames[i];
            builder.Append(sample.Frame).Append(',');
            builder.Append(sample.ElapsedSeconds.ToString("F4", Culture)).Append(',');
            builder.Append(sample.FrameMilliseconds.ToString("F4", Culture)).Append(',');
            builder.Append(sample.GCAllocatedBytes).Append(',');
            builder.Append(sample.GCUsedBytes).Append(',');
            builder.Append(sample.SystemUsedBytes).AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private void SaveSummary(string path, bool completed)
    {
        long gcTotalBytes = 0;
        long gcMaxBytes = 0;
        long lastGCUsedBytes = -1;
        long lastSystemUsedBytes = -1;
        int gcValidSamples = 0;
        double totalFrameMs = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            FrameSample sample = frames[i];
            totalFrameMs += sample.FrameMilliseconds;

            if (sample.GCAllocatedBytes >= 0)
            {
                gcTotalBytes += sample.GCAllocatedBytes;
                gcMaxBytes = Math.Max(gcMaxBytes, sample.GCAllocatedBytes);
                gcValidSamples++;
            }

            if (sample.GCUsedBytes >= 0)
                lastGCUsedBytes = sample.GCUsedBytes;

            if (sample.SystemUsedBytes >= 0)
                lastSystemUsedBytes = sample.SystemUsedBytes;
        }

        StringBuilder builder = new StringBuilder(1024);
        builder.AppendLine("metric,value");
        AppendMetric(builder, "scenario", scenarioName);
        AppendMetric(builder, "completed", completed ? "true" : "false");
        AppendMetric(builder, "unity_version", Application.unityVersion);
        AppendMetric(builder, "platform", Application.platform.ToString());
        AppendMetric(builder, "frame_count", frames.Count.ToString(Culture));
        AppendMetric(builder, "gc_valid_samples", gcValidSamples.ToString(Culture));
        AppendMetric(builder, "average_frame_ms", Average(totalFrameMs, frames.Count));
        AppendMetric(builder, "average_gc_alloc_bytes_per_frame", Average(gcTotalBytes, gcValidSamples));
        AppendMetric(builder, "total_gc_alloc_bytes_observed", gcTotalBytes.ToString(Culture));
        AppendMetric(builder, "maximum_gc_alloc_bytes_per_frame", gcValidSamples == 0 ? "unavailable" : gcMaxBytes.ToString(Culture));
        AppendMetric(builder, "last_gc_used_bytes", lastGCUsedBytes.ToString(Culture));
        AppendMetric(builder, "last_system_used_bytes", lastSystemUsedBytes.ToString(Culture));
        AppendMetric(builder, "allocation_callstacks_requested", saveProfilerRaw && captureAllocationCallstacks ? "true" : "false");
        AppendMetric(builder, "raw_profiler_requested", saveProfilerRaw ? "true" : "false");
        AppendMetric(builder, "raw_profiler_file", saveProfilerRaw ? profilerRawPath : "none");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Average(double total, int count)
    {
        return count > 0 ? (total / count).ToString("F3", Culture) : "unavailable";
    }

    private static void AppendMetric(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append(',').Append(value).AppendLine();
    }

    private static long ReadLastValue(ProfilerRecorder recorder)
    {
        return recorder.Valid ? recorder.LastValue : -1;
    }

    private static string CleanFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "GC_Capture";

        string result = value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();

        for (int i = 0; i < invalid.Length; i++)
            result = result.Replace(invalid[i], '_');

        return result;
    }
}
