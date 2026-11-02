using UnityEngine;
using UnityEngine.Profiling;

public class PerformanceMonitor : MonoBehaviour
{
    [SerializeField] bool showMonitor = true;
    [SerializeField] int targetFrameRate = 60;

    private float fps;
    private float frameTime;
    private float memory;

    private GUIStyle labelStyle;

    private void Awake()
    {
        Application.targetFrameRate = targetFrameRate;

        labelStyle = new GUIStyle();
        labelStyle.fontSize = 24;
        labelStyle.normal.textColor = Color.white;
    }

    private void Update()
    {
        frameTime = Time.unscaledDeltaTime * 1000f;
        fps = 1f / Time.unscaledDeltaTime;
        memory = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
    }

    private void OnGUI()
    {
        if (!showMonitor)
            return;

        GUI.Label(
            new Rect(20f, 20f, 400f, 40f),
            $"FPS : {fps:F1}",
            labelStyle
        );

        GUI.Label(
            new Rect(20f, 50f, 400f, 40f),
            $"Frame Time : {frameTime:F2} ms",
            labelStyle
        );

        GUI.Label(
            new Rect(20f, 80f, 400f, 40f),
            $"Memory : {memory:F1} MB",
            labelStyle
        );

        GUI.Label(
            new Rect(20f, 110f, 400f, 40f),
            $"Time Scale : {Time.timeScale:F2}",
            labelStyle
        );
    }
}