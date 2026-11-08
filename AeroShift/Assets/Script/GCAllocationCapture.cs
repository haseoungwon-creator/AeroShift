
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
    [Header("Capture")]
    [SerializeField] private bool startAutomaticallyWhenPlaying = true;
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [SerializeField, Min(1f)] private float captureSeconds = 30f;
    [SerializeField] private string scenarioName = "3D_Flight_GC";

    [Header("Profiler")]
    [SerializeField] private bool saveProfilerRaw;
    [SerializeField] private bool captureAllocationCallstacks;
    [SerializeField, Range(16, 1024)] private int profilerBufferMiB = 128;

    [Header("Cloud Metrics")]
    [SerializeField] private bool captureCloudMetrics = true;
    [SerializeField] private CloudSpawner cloudSpawner;
    [SerializeField] private PoolManager poolManager;
    [SerializeField, Min(1)] private int cloudSampleIntervalFrames = 10;

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

    private struct CloudSample
    {
        public int Frame;
        public float ElapsedSeconds;
        public int InitialTargetCount;
        public int InitialPlacedCount;
        public int ActiveCloudCount;
        public float EffectiveSpawnDistance;
        public bool SpawnRangeLimited;
        public int CloudRentFailures;
        public int PoolCreatedCount;
        public int PoolActiveCount;
        public int PoolPeakActiveCount;
        public int PoolAvailableCount;
        public int PoolFailedRentCount;
        public int PoolRuntimeCreationCount;
    }

    private readonly List<FrameSample> frames =
        new List<FrameSample>(8192);

    private readonly List<CloudSample> cloudSamples =
        new List<CloudSample>(512);

    private static readonly CultureInfo Culture =
        CultureInfo.InvariantCulture;

    private CaptureState state;
    private bool manualCapture;

    private float warmupStart;
    private float captureStart;

    private string outputDirectory;
    private string outputPrefix;
    private string profilerRawPath;

    private int lastCloudSampleFrame = -1;

    private ProfilerRecorder gcAllocatedRecorder;
    private ProfilerRecorder gcUsedRecorder;
    private ProfilerRecorder systemUsedRecorder;

    private bool previousProfilerEnabled;
    private bool previousBinaryLogEnabled;
    private bool previousCallstacksEnabled;
    private string previousLogFile;
    private int previousProfilerMemory;
    private bool profilerStateSaved;

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

            if (Time.unscaledTime - warmupStart >=
                Mathf.Max(0f, warmupSeconds))
            {
                BeginCapture();
            }

            return;
        }

        if (state != CaptureState.Capturing)
            return;

        if (!manualCapture && !IsPlaying())
        {
            FinishCapture(false);
            return;
        }

        if (Time.unscaledTime - captureStart >=
            Mathf.Max(1f, captureSeconds))
        {
            FinishCapture(true);
        }
    }

    private void LateUpdate()
    {
        if (state != CaptureState.Capturing)
            return;

        frames.Add(new FrameSample
        {
            Frame = Time.frameCount,

            ElapsedSeconds =
                Time.unscaledTime - captureStart,

            FrameMilliseconds =
                Time.unscaledDeltaTime * 1000f,

            GCAllocatedBytes =
                ReadLastValue(gcAllocatedRecorder),

            GCUsedBytes =
                ReadLastValue(gcUsedRecorder),

            SystemUsedBytes =
                ReadLastValue(systemUsedRecorder)
        });

        if (!captureCloudMetrics || cloudSpawner == null)
            return;

        if (lastCloudSampleFrame < 0 ||
            Time.frameCount - lastCloudSampleFrame >=
            cloudSampleIntervalFrames)
        {
            RecordCloudSample();
        }
    }

    public bool ConfigureScenarioName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            state == CaptureState.Capturing)
            return false;

        scenarioName = value.Trim();
        return true;
    }

    [ContextMenu("Start GC Capture Now")]
    public void StartCaptureNow()
    {
        if (!Application.isPlaying ||
            state == CaptureState.Capturing)
            return;

        manualCapture = true;
        warmupStart = Time.unscaledTime;
        state = CaptureState.WarmingUp;
    }

    [ContextMenu("Stop GC Capture")]
    public void StopCapture()
    {
        if (state == CaptureState.Capturing)
        {
            FinishCapture(false);
        }
        else if (state == CaptureState.WarmingUp)
        {
            state = CaptureState.Finished;
        }
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
        GameManager manager = GameManager.Instance;

        return manager != null &&
            manager.CurrentState == GameStates.Playing;
    }

    private void BeginCapture()
    {
        frames.Clear();
        cloudSamples.Clear();

        lastCloudSampleFrame = -1;

        try
        {
            if (captureCloudMetrics)
            {
                if (cloudSpawner == null)
                {
                    cloudSpawner =
                        FindFirstObjectByType<CloudSpawner>();
                }

                if (poolManager == null)
                {
                    poolManager =
                        FindFirstObjectByType<PoolManager>();
                }
            }

            string cleanScenario =
                CleanFileName(scenarioName);

            outputDirectory =
                GetExperimentOutputDirectory(cleanScenario);

            Directory.CreateDirectory(outputDirectory);

            outputPrefix =
                cleanScenario + "_" +
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss_fff",
                    Culture
                );

            profilerRawPath = Path.Combine(
                outputDirectory,
                outputPrefix + "_profile.raw"
            );

            if (saveProfilerRaw)
            {
                SaveProfilerSettings();

                Profiler.enabled = false;

                Profiler.maxUsedMemory =
                    Mathf.Clamp(
                        profilerBufferMiB,
                        16,
                        1024
                    ) * 1024 * 1024;

                Profiler.logFile = profilerRawPath;
                Profiler.enableBinaryLog = true;

                Profiler.enableAllocationCallstacks =
                    captureAllocationCallstacks;

                Profiler.enabled = true;
            }

            gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC Allocated In Frame",
                1
            );

            gcUsedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC Used Memory",
                1
            );

            systemUsedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "System Used Memory",
                1
            );

            captureStart = Time.unscaledTime;
            state = CaptureState.Capturing;

            if (captureCloudMetrics && cloudSpawner != null)
                RecordCloudSample();
        }
        catch (Exception exception)
        {
            DisposeRecorders();
            RestoreProfilerSettings();

            state = CaptureState.Finished;

            Debug.LogException(exception, this);
        }
    }

    private void RecordCloudSample()
    {
        if (cloudSpawner == null)
            return;

        CloudSample sample = new CloudSample
        {
            Frame = Time.frameCount,

            ElapsedSeconds =
                Time.unscaledTime - captureStart,

            InitialTargetCount =
                cloudSpawner.InitialTargetCount,

            InitialPlacedCount =
                cloudSpawner.InitialPlacedCount,

            ActiveCloudCount =
                cloudSpawner.ActiveCloudCount,

            EffectiveSpawnDistance =
                cloudSpawner.EffectiveSpawnDistance,

            SpawnRangeLimited =
                cloudSpawner.SpawnRangeLimited,

            CloudRentFailures =
                cloudSpawner.CloudRentFailures,

            PoolCreatedCount = -1,
            PoolActiveCount = -1,
            PoolPeakActiveCount = -1,
            PoolAvailableCount = -1,
            PoolFailedRentCount = -1,
            PoolRuntimeCreationCount = -1
        };

        if (poolManager != null)
        {
            PoolId id = PoolId.Cloud;

            sample.PoolCreatedCount =
                poolManager.GetCreatedCount(id);

            sample.PoolActiveCount =
                poolManager.GetActiveCount(id);

            sample.PoolPeakActiveCount =
                poolManager.GetPeakActiveCount(id);

            sample.PoolAvailableCount =
                poolManager.GetAvailableCount(id);

            sample.PoolFailedRentCount =
                poolManager.GetFailedRentCount(id);

            sample.PoolRuntimeCreationCount =
                poolManager.GetRuntimeCreationCount(id);
        }

        cloudSamples.Add(sample);

        lastCloudSampleFrame = Time.frameCount;
    }

    private void FinishCapture(bool completed)
    {
        if (state != CaptureState.Capturing)
            return;

        if (captureCloudMetrics && cloudSpawner != null &&
            lastCloudSampleFrame != Time.frameCount)
        {
            RecordCloudSample();
        }

        state = CaptureState.Finished;

        DisposeRecorders();
        RestoreProfilerSettings();

        try
        {
            string framesPath = Path.Combine(
                outputDirectory,
                outputPrefix + "_frames.csv"
            );

            string summaryPath = Path.Combine(
                outputDirectory,
                outputPrefix + "_summary.csv"
            );

            SaveFrames(framesPath);
            SaveSummary(summaryPath, completed);

            if (cloudSamples.Count > 0)
            {
                string cloudPath = Path.Combine(
                    outputDirectory,
                    outputPrefix + "_cloud_metrics.csv"
                );

                SaveCloudMetrics(cloudPath);
            }

            Debug.Log(
                "GC Allocation Capture ¿Ï·á: " +
                outputDirectory,
                this
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private void SaveFrames(string path)
    {
        StringBuilder builder = new StringBuilder(
            Math.Max(1024, frames.Count * 72)
        );

        builder.AppendLine(
            "frame,elapsed_seconds,frame_ms," +
            "gc_alloc_bytes,gc_used_bytes,system_used_bytes"
        );

        for (int i = 0; i < frames.Count; i++)
        {
            FrameSample sample = frames[i];

            builder.Append(sample.Frame).Append(',');
            builder.Append(
                sample.ElapsedSeconds.ToString("F4", Culture)
            ).Append(',');

            builder.Append(
                sample.FrameMilliseconds.ToString("F4", Culture)
            ).Append(',');

            builder.Append(sample.GCAllocatedBytes).Append(',');
            builder.Append(sample.GCUsedBytes).Append(',');
            builder.Append(sample.SystemUsedBytes).AppendLine();
        }

        File.WriteAllText(
            path,
            builder.ToString(),
            new UTF8Encoding(false)
        );
    }

    private void SaveCloudMetrics(string path)
    {
        StringBuilder builder = new StringBuilder(
            Math.Max(1024, cloudSamples.Count * 128)
        );

        builder.AppendLine(
            "frame,elapsed_seconds," +
            "initial_target_count,initial_placed_count," +
            "active_cloud_count,effective_spawn_distance," +
            "spawn_range_limited,cloud_rent_failures," +
            "pool_created_count,pool_active_count," +
            "pool_peak_active_count,pool_available_count," +
            "pool_failed_rent_count,pool_runtime_creation_count"
        );

        for (int i = 0; i < cloudSamples.Count; i++)
        {
            CloudSample sample = cloudSamples[i];

            builder.Append(sample.Frame).Append(',');

            builder.Append(
                sample.ElapsedSeconds.ToString("F4", Culture)
            ).Append(',');

            builder.Append(sample.InitialTargetCount).Append(',');
            builder.Append(sample.InitialPlacedCount).Append(',');
            builder.Append(sample.ActiveCloudCount).Append(',');

            builder.Append(
                sample.EffectiveSpawnDistance.ToString(
                    "F3",
                    Culture
                )
            ).Append(',');

            builder.Append(
                sample.SpawnRangeLimited ? "true" : "false"
            ).Append(',');

            builder.Append(sample.CloudRentFailures).Append(',');
            builder.Append(sample.PoolCreatedCount).Append(',');
            builder.Append(sample.PoolActiveCount).Append(',');
            builder.Append(sample.PoolPeakActiveCount).Append(',');
            builder.Append(sample.PoolAvailableCount).Append(',');
            builder.Append(sample.PoolFailedRentCount).Append(',');
            builder.Append(
                sample.PoolRuntimeCreationCount
            ).AppendLine();
        }

        File.WriteAllText(
            path,
            builder.ToString(),
            new UTF8Encoding(false)
        );
    }

    private void SaveSummary(string path, bool completed)
    {
        long gcTotalBytes = 0;
        long gcMaxBytes = 0;

        long lastGCUsedBytes = -1;
        long lastSystemUsedBytes = -1;

        int peakSampleFrame = -1;
        double peakSampleFrameMs = -1d;
        long peakSampleGCUsedBytes = -1;
        long peakSampleSystemUsedBytes = -1;

        int gcValidSamples = 0;
        double totalFrameMs = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            FrameSample sample = frames[i];

            totalFrameMs += sample.FrameMilliseconds;

            if (sample.GCAllocatedBytes >= 0)
            {
                gcTotalBytes += sample.GCAllocatedBytes;
                gcValidSamples++;

                if (peakSampleFrame < 0 ||
                    sample.GCAllocatedBytes > gcMaxBytes)
                {
                    gcMaxBytes = sample.GCAllocatedBytes;
                    peakSampleFrame = sample.Frame;
                    peakSampleFrameMs =
                        sample.FrameMilliseconds;

                    peakSampleGCUsedBytes =
                        sample.GCUsedBytes;

                    peakSampleSystemUsedBytes =
                        sample.SystemUsedBytes;
                }
            }

            if (sample.GCUsedBytes >= 0)
                lastGCUsedBytes = sample.GCUsedBytes;

            if (sample.SystemUsedBytes >= 0)
                lastSystemUsedBytes = sample.SystemUsedBytes;
        }

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("metric,value");

        AppendMetric(builder, "scenario", scenarioName);
        AppendMetric(
            builder,
            "completed",
            completed ? "true" : "false"
        );

        AppendMetric(
            builder,
            "unity_version",
            Application.unityVersion
        );

        AppendMetric(
            builder,
            "platform",
            Application.platform.ToString()
        );

        AppendMetric(
            builder,
            "frame_count",
            frames.Count.ToString(Culture)
        );

        AppendMetric(
            builder,
            "gc_valid_samples",
            gcValidSamples.ToString(Culture)
        );

        AppendMetric(
            builder,
            "average_frame_ms",
            Average(totalFrameMs, frames.Count)
        );

        AppendMetric(
            builder,
            "average_gc_alloc_bytes_per_frame",
            Average(gcTotalBytes, gcValidSamples)
        );

        AppendMetric(
            builder,
            "total_gc_alloc_bytes_observed",
            gcTotalBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "maximum_gc_alloc_bytes_per_frame",
            gcValidSamples == 0
                ? "unavailable"
                : gcMaxBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "peak_gc_sample_frame",
            peakSampleFrame.ToString(Culture)
        );

        AppendMetric(
            builder,
            "peak_gc_sample_frame_ms",
            peakSampleFrame < 0
                ? "unavailable"
                : peakSampleFrameMs.ToString("F4", Culture)
        );

        AppendMetric(
            builder,
            "peak_gc_sample_gc_used_bytes",
            peakSampleGCUsedBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "peak_gc_sample_system_used_bytes",
            peakSampleSystemUsedBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "last_gc_used_bytes",
            lastGCUsedBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "last_system_used_bytes",
            lastSystemUsedBytes.ToString(Culture)
        );

        AppendMetric(
            builder,
            "allocation_callstacks_requested",
            saveProfilerRaw && captureAllocationCallstacks
                ? "true"
                : "false"
        );

        AppendMetric(
            builder,
            "raw_profiler_requested",
            saveProfilerRaw ? "true" : "false"
        );

        AppendMetric(
            builder,
            "raw_profiler_file",
            saveProfilerRaw ? profilerRawPath : "none"
        );

        AppendMetric(
            builder,
            "cloud_metrics_enabled",
            captureCloudMetrics ? "true" : "false"
        );

        AppendMetric(
            builder,
            "cloud_sample_count",
            cloudSamples.Count.ToString(Culture)
        );

        if (cloudSamples.Count > 0)
        {
            CloudSample first = cloudSamples[0];
            CloudSample last =
                cloudSamples[cloudSamples.Count - 1];

            int observedPeakActive = 0;

            for (int i = 0; i < cloudSamples.Count; i++)
            {
                observedPeakActive = Mathf.Max(
                    observedPeakActive,
                    cloudSamples[i].ActiveCloudCount
                );
            }

            AppendMetric(
                builder,
                "cloud_initial_target_count",
                last.InitialTargetCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_initial_placed_count",
                last.InitialPlacedCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_active_start",
                first.ActiveCloudCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_active_end",
                last.ActiveCloudCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_active_peak_observed",
                observedPeakActive.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_effective_spawn_distance",
                last.EffectiveSpawnDistance.ToString(
                    "F3",
                    Culture
                )
            );

            AppendMetric(
                builder,
                "cloud_spawn_range_limited",
                last.SpawnRangeLimited ? "true" : "false"
            );

            AppendMetric(
                builder,
                "cloud_rent_failures_during_capture",
                (last.CloudRentFailures -
                 first.CloudRentFailures).ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_pool_created_start",
                first.PoolCreatedCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_pool_created_end",
                last.PoolCreatedCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_pool_peak_active_end",
                last.PoolPeakActiveCount.ToString(Culture)
            );

            AppendMetric(
                builder,
                "cloud_pool_runtime_creation_delta",
                first.PoolRuntimeCreationCount < 0 ||
                last.PoolRuntimeCreationCount < 0
                    ? "unavailable"
                    : (last.PoolRuntimeCreationCount -
                       first.PoolRuntimeCreationCount).ToString(
                        Culture
                    )
            );
        }

        AppendMetric(
            builder,
            "output_directory",
            outputDirectory
        );

        File.WriteAllText(
            path,
            builder.ToString(),
            new UTF8Encoding(false)
        );
    }

    private void SaveProfilerSettings()
    {
        previousProfilerEnabled = Profiler.enabled;
        previousBinaryLogEnabled = Profiler.enableBinaryLog;
        previousCallstacksEnabled =
            Profiler.enableAllocationCallstacks;

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

        Profiler.enableAllocationCallstacks =
            previousCallstacksEnabled;

        Profiler.maxUsedMemory = previousProfilerMemory;

        if (!string.IsNullOrEmpty(previousLogFile))
            Profiler.logFile = previousLogFile;

        Profiler.enableBinaryLog =
            previousBinaryLogEnabled;

        Profiler.enabled = previousProfilerEnabled;

        profilerStateSaved = false;
    }

    private void DisposeRecorders()
    {
        gcAllocatedRecorder.Dispose();
        gcUsedRecorder.Dispose();
        systemUsedRecorder.Dispose();
    }

    private static long ReadLastValue(
        ProfilerRecorder recorder)
    {
        return recorder.Valid ? recorder.LastValue : -1;
    }

    private static string Average(double total, int count)
    {
        return count > 0
            ? (total / count).ToString("F3", Culture)
            : "unavailable";
    }

    private static void AppendMetric(
        StringBuilder builder,
        string name,
        string value)
    {
        builder.Append(name)
            .Append(',')
            .Append(value)
            .AppendLine();
    }

    private static string GetExperimentOutputDirectory(
        string cleanScenario)
    {
        string root = Path.Combine(
            Application.persistentDataPath,
            "AeroBenchmarks",
            "Experiments"
        );

        string[] parts = cleanScenario.Split('_');

        if (parts.Length == 5 &&
            (parts[0] == "AeroShift" ||
             parts[0] == "AeroFlip") &&
            (parts[3] == "ON" ||
             parts[3] == "OFF") &&
            parts[4].StartsWith(
                "Run",
                StringComparison.Ordinal
            ) &&
            int.TryParse(
                parts[4].Substring(3),
                out int runNumber
            ) &&
            runNumber > 0 &&
            !string.IsNullOrWhiteSpace(parts[1]) &&
            !string.IsNullOrWhiteSpace(parts[2]))
        {
            return Path.Combine(
                root,
                parts[1],
                parts[2],
                parts[3],
                "Run" + runNumber.ToString("D2", Culture)
            );
        }

        return Path.Combine(root, "Other", cleanScenario);
    }

    private static string CleanFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "GC_Capture";

        string result = value.Trim();

        char[] invalid =
            Path.GetInvalidFileNameChars();

        for (int i = 0; i < invalid.Length; i++)
        {
            result = result.Replace(
                invalid[i],
                '_'
            );
        }

        return result;
    }
}
