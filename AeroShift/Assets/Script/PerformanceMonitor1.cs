using Unity.Profiling;
using UnityEngine;

public class PerformanceMonitor1 : MonoBehaviour
{
    [SerializeField] float measureDuration = 5f;

    private ProfilerRecorder gcAllocRecorder;
    private ProfilerRecorder mainThreadTimeRecorder;

    private float timer;
    private float gcTotal;
    private float gcMax;
    private float mainThreadTotal;
    private float mainThreadMax;
    private int sampleCount;

    private void OnEnable()
    {
        gcAllocRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Memory,
            "GC Allocated In Frame");

        mainThreadTimeRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal,
            "Main Thread");

        ResetMeasurement();
    }

    private void Update()
    {
        if (!gcAllocRecorder.Valid || !mainThreadTimeRecorder.Valid)
            return;

        float gcAllocKB = gcAllocRecorder.LastValue / 1024f;
        float mainThreadMs = mainThreadTimeRecorder.LastValue / 1000000f;

        gcTotal += gcAllocKB;
        gcMax = Mathf.Max(gcMax, gcAllocKB);

        mainThreadTotal += mainThreadMs;
        mainThreadMax = Mathf.Max(mainThreadMax, mainThreadMs);

        sampleCount++;
        timer += Time.unscaledDeltaTime;

        if (timer < measureDuration)
            return;

        PrintResult();
        ResetMeasurement();
    }

    private void PrintResult()
    {
        if (sampleCount == 0)
            return;

        float gcAverage = gcTotal / sampleCount;
        float mainThreadAverage = mainThreadTotal / sampleCount;

        Debug.Log(
            $"Performance Result | " +
            $"GC Avg: {gcAverage:F2} KB/frame | " +
            $"GC Max: {gcMax:F2} KB/frame | " +
            $"Main Thread Avg: {mainThreadAverage:F2} ms | " +
            $"Main Thread Max: {mainThreadMax:F2} ms");
    }

    private void ResetMeasurement()
    {
        timer = 0f;

        gcTotal = 0f;
        gcMax = 0f;

        mainThreadTotal = 0f;
        mainThreadMax = 0f;

        sampleCount = 0;
    }

    private void OnDisable()
    {
        gcAllocRecorder.Dispose();
        mainThreadTimeRecorder.Dispose();
    }
}