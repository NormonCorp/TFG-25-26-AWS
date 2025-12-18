using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;

public class SessionTelemetryRecorder : MonoBehaviour
{
    [Header("Refs")]
    public JumpComponent playerJump;     // para contar SOLO saltos reales
    public TMP_Text debugText;           // opcional: mostrar métricas en UI

    [Header("Save")]
    public bool autosave = true;
    public float autosaveEverySeconds = 15f;
    public string fileNamePrefix = "session_telemetry_";

    private SessionTelemetry data;
    private float nextAutosaveTime;

    // Ventanas deslizantes de eventos (saltos reales)
    private readonly Queue<float> last10s = new Queue<float>();
    private readonly Queue<float> last60s = new Queue<float>();

    [Serializable]
    public class SessionTelemetry
    {
        public string sessionId;
        public string startedAtUtc;
        public string lastSavedAtUtc;

        public float timePlayedSeconds;

        public int score;         // aquí score = saltos reales
        public int validKeyCount; // “teclas válidas” (saltos reales)

        public float keysPerSecondAvg;
        public float keysPerMinuteAvg;

        public float keysPerSecondLast10;
        public int keysPerMinuteLast60;

        public string notes; // por si quieres añadir info extra
    }

    private float startRealtime;

    void Start()
    {
        startRealtime = Time.realtimeSinceStartup;

        data = new SessionTelemetry
        {
            sessionId = Guid.NewGuid().ToString("N"),
            startedAtUtc = DateTime.UtcNow.ToString("o"),
            lastSavedAtUtc = "",
            notes = ""
        };

        if (playerJump != null)
            playerJump.OnJump += OnRealJump;

        nextAutosaveTime = Time.realtimeSinceStartup + autosaveEverySeconds;

        UpdateDerivedStats();
        UpdateDebugUI();
    }

    void OnDestroy()
    {
        if (playerJump != null)
            playerJump.OnJump -= OnRealJump;
    }

    void Update()
    {
        // Actualiza tiempo jugado y métricas derivadas
        UpdateDerivedStats();
        UpdateDebugUI();

        // Autosave
        if (autosave && Time.realtimeSinceStartup >= nextAutosaveTime)
        {
            nextAutosaveTime = Time.realtimeSinceStartup + autosaveEverySeconds;
            SaveToDisk();
        }
    }

    private void OnApplicationQuit()
    {
        SaveToDisk();
    }

    private void OnRealJump()
    {
        // salto real => incrementa score y conteo de “teclas válidas”
        data.score++;
        data.validKeyCount++;

        float now = Time.realtimeSinceStartup;
        last10s.Enqueue(now);
        last60s.Enqueue(now);
        PruneQueues(now);

        UpdateDerivedStats();
        UpdateDebugUI();
    }

    private void PruneQueues(float now)
    {
        while (last10s.Count > 0 && now - last10s.Peek() > 10f) last10s.Dequeue();
        while (last60s.Count > 0 && now - last60s.Peek() > 60f) last60s.Dequeue();
    }

    private void UpdateDerivedStats()
    {
        float now = Time.realtimeSinceStartup;
        float played = now - startRealtime;

        data.timePlayedSeconds = Mathf.Max(0f, played);

        PruneQueues(now);

        if (data.timePlayedSeconds > 0.0001f)
        {
            data.keysPerSecondAvg = data.validKeyCount / data.timePlayedSeconds;
            data.keysPerMinuteAvg = data.keysPerSecondAvg * 60f;
        }
        else
        {
            data.keysPerSecondAvg = 0f;
            data.keysPerMinuteAvg = 0f;
        }

        data.keysPerSecondLast10 = last10s.Count / 10f;
        data.keysPerMinuteLast60 = last60s.Count; // ya es ventana 60s => “por minuto”
    }

    private void UpdateDebugUI()
    {
        if (debugText == null) return;

        debugText.text =
            $"Score: {data.score}\n" +
            $"Time: {data.timePlayedSeconds:F1}s\n" +
            $"KeysTotal(valid): {data.validKeyCount}\n" +
            $"KPS avg: {data.keysPerSecondAvg:F2}\n" +
            $"KPM avg: {data.keysPerMinuteAvg:F1}\n" +
            $"KPS last10: {data.keysPerSecondLast10:F2}\n" +
            $"KPM last60: {data.keysPerMinuteLast60}";
    }

public void SaveToDisk()
{
#if UNITY_EDITOR
    data.lastSavedAtUtc = DateTime.UtcNow.ToString("o");

    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    string shortSession = data.sessionId.Substring(0, 6);

    string fileName =
        $"{fileNamePrefix}{timestamp}_{shortSession}.json";

    // Raíz del proyecto (Assets/..)
    string projectRoot = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..")
    );

    string path = Path.Combine(projectRoot, fileName);

    string json = JsonUtility.ToJson(data, prettyPrint: true);
    File.WriteAllText(path, json);

    Debug.Log($"Telemetry saved at:\n{path}");
#else
    Debug.LogWarning("SaveToDisk skipped: writing to project root is editor-only.");
#endif
}

public string GetSavePath()
{
#if UNITY_EDITOR
    string projectRoot = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..")
    );
    return projectRoot;
#else
    return Application.persistentDataPath;
#endif
}



    public SessionTelemetry GetCurrentSnapshot()
    {
        // Devuelve una copia “lógica” (referencia) del estado actual.
        // Si quieres copia profunda, lo hago, pero para prototipo va perfecto.
        return data;
    }
}
