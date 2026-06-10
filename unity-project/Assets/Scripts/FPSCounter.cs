using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    public int targetFrameRate = 60;

    float deltaTime;

    float logTimer;
    int logFrameCount;

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        LogAverageFPS();
    }

    void OnGUI()
    {
        float fps = 1.0f / deltaTime;
        GUI.Label(new Rect(10, 10, 200, 30), $"FPS: {fps:0}");
    }

    void LogAverageFPS()
    {
        logTimer += Time.unscaledDeltaTime;
        logFrameCount++;

        if (logTimer >= 1f)
        {
            float averageFps = logFrameCount / logTimer;
            Debug.Log($"Average FPS: {averageFps:F1}");

            logTimer = 0f;
            logFrameCount = 0;
        }
    }
}