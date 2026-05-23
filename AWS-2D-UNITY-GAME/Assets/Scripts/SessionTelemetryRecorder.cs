using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SessionTelemetryRecorder : MonoBehaviour
{
    [Header("Refs")]
    public JumpComponent playerJump;
    public TMP_Text debugText;
    public ScoreComponent statsManager;
    public DynamoDBManager dbManager;

    [Header("Save")]
    public bool autosave = true;
    public float autosaveEverySeconds = 15f;
    public string fileNamePrefix = "session_telemetry_";

    [Header("Security")]
    public float intrusionCheckEverySeconds = 3f;

    private SessionTelemetry data;
    private float nextAutosaveTime;
    private float nextIntrusionCheckTime;
    private const string CurrentSaveFileName = "session_telemetry.json";

    // Hasta que la nube no haya respondido y aplicado el score real, no
    // contamos saltos ni guardamos: si no, el spam de espacio sobrescribe
    // el score de la nube con un valor pequeño antes de leerlo.
    private bool cloudReady = false;
    private bool exitRequested = false;
    private Canvas overlayCanvas;
    private CanvasGroup intrusionNoticeGroup;
    private TextMeshProUGUI intrusionNoticeText;
    private Coroutine intrusionNoticeRoutine;

    private readonly Queue<float> last10s = new Queue<float>();
    private readonly Queue<float> last60s = new Queue<float>();

    [Serializable]
    public class SessionTelemetry
    {
        public string sessionId;
        public string userId;
        public string startedAtUtc;
        public string lastSavedAtUtc;
        public float  timePlayedSeconds;
        public int    score;
        public int    validKeyCount;
        public float  keysPerSecondAvg;
        public float  keysPerMinuteAvg;
        public float  keysPerSecondLast10;
        public int    keysPerMinuteLast60;
        public string notes;
    }

    private float startRealtime;

    void Start()
    {
        startRealtime = Time.realtimeSinceStartup;
        data = new SessionTelemetry
        {
            sessionId         = Guid.NewGuid().ToString("N"),
            startedAtUtc      = DateTime.UtcNow.ToString("o"),
            score             = 0,
            timePlayedSeconds = 0
        };
        nextAutosaveTime = Time.realtimeSinceStartup + autosaveEverySeconds;
        nextIntrusionCheckTime = Time.realtimeSinceStartup + intrusionCheckEverySeconds;

        if (dbManager == null) dbManager = DynamoDBManager.Instance;
        data.userId = GetCurrentUserId();

        if (DynamoDBManager.Instance != null)
        {
            DynamoDBManager.Instance.RegisterNewSession(() =>
            {
                string latestLocalFile = GetLatestLocalSave();
                if (!string.IsNullOrEmpty(latestLocalFile))
                {
                    string jsonLocal = File.ReadAllText(latestLocalFile);
                    VerifyStartupData(jsonLocal);
                }
                else
                {
                    VerifyStartupData("");
                }
            });
        }
        else
        {
            // Sin backend (offline/editor sin configurar): juega de inmediato.
            cloudReady = true;
        }

        if (playerJump != null) playerJump.OnJump += OnRealJump;
        CreateExitButton();
        UpdateDerivedStats();
        UpdateDebugUI();
    }

    private void LoadCloudData()
    {
        DynamoDBManager.Instance.LoadData((puntosNube, tiempoNube) =>
        {
            ApplyCloudStats(puntosNube, tiempoNube);
            Debug.Log($"Progreso restaurado desde DynamoDB: {puntosNube} pts.");
        });
    }

    private void VerifyStartupData(string envelopeJson)
    {
        if (!IsLocalSaveForCurrentUser(envelopeJson))
        {
            Debug.Log("No hay save local valido para esta cuenta. Forzando nube.");
            envelopeJson = "";
        }

        bool forceCloudApplied = false;
        DynamoDBManager.Instance.VerifyDataAtStartup(
            envelopeJson,
            () =>
            {
                if (!forceCloudApplied) LoadCloudData();
            },
            (puntosNube, tiempoNube) =>
            {
                forceCloudApplied = true;
                ApplyCloudStats(puntosNube, tiempoNube);
                Debug.Log($"Progreso restaurado desde Lambda FORCE_CLOUD: {puntosNube} pts.");
            });
    }

    private void ApplyCloudStats(int puntosNube, float tiempoNube)
    {
        float elapsedThisSession = Mathf.Max(0f, Time.realtimeSinceStartup - startRealtime);
        data.userId = GetCurrentUserId();
        data.score = puntosNube;
        data.timePlayedSeconds = tiempoNube + elapsedThisSession;

        if (statsManager != null)
        {
            statsManager.ApplyCloudStats(puntosNube, tiempoNube);
        }

        cloudReady = true;
        UpdateDebugUI();
    }

    private string GetLatestLocalSave()
    {
        string dir = GetSavePath();
        if (!Directory.Exists(dir)) return null;

        string currentSave = GetCurrentLocalSavePath();
        if (File.Exists(currentSave)) return currentSave;

        string[] files = Directory.GetFiles(dir, fileNamePrefix + "*.json");
        if (files.Length == 0) return null;

        Array.Sort(files);
        return files[files.Length - 1];
    }

    private bool IsLocalSaveForCurrentUser(string envelopeJson)
    {
        if (string.IsNullOrEmpty(envelopeJson) || envelopeJson.Trim().Length == 0) return false;

        try
        {
            SecurePayload envelope = JsonUtility.FromJson<SecurePayload>(envelopeJson);
            if (envelope == null || string.IsNullOrEmpty(envelope.data)) return false;

            SessionTelemetry localData = JsonUtility.FromJson<SessionTelemetry>(envelope.data);
            if (localData == null || string.IsNullOrEmpty(localData.userId)) return false;

            return localData.userId == GetCurrentUserId();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Save local ilegible. Forzando nube: " + e.Message);
            return false;
        }
    }

    void OnDestroy()
    {
        if (playerJump != null) playerJump.OnJump -= OnRealJump;
    }

    void Update()
    {
        UpdateDerivedStats();
        UpdateDebugUI();

        if (!exitRequested && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ReturnToLogin();
            return;
        }

        if (autosave && cloudReady && dbManager != null && Time.realtimeSinceStartup >= nextAutosaveTime)
        {
            nextAutosaveTime = Time.realtimeSinceStartup + autosaveEverySeconds;
            dbManager.SaveGameData(data.score, data.timePlayedSeconds);
        }

        if (cloudReady && dbManager != null && Time.realtimeSinceStartup >= nextIntrusionCheckTime)
        {
            nextIntrusionCheckTime = Time.realtimeSinceStartup + intrusionCheckEverySeconds;
            dbManager.CheckIntrusionNotice(ShowIntrusionNotice);
        }

        if (cloudReady && dbManager != null && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            dbManager.SaveGameData(data.score, data.timePlayedSeconds);
        }
    }

    private void ReturnToLogin()
    {
        exitRequested = true;

        if (DynamoDBManager.Instance != null)
        {
            DynamoDBManager.Instance.UnregisterCurrentSession(() =>
            {
                DynamoDBManager.Instance.SignOutAWS();
                ClearAuthPrefs();
                SceneManager.LoadScene("LoginScene");
            });
            return;
        }

        ClearAuthPrefs();
        SceneManager.LoadScene("LoginScene");
    }

    private void QuitGame()
    {
        if (exitRequested) return;
        exitRequested = true;
        Application.Quit();
    }

    private void ClearAuthPrefs()
    {
        PlayerPrefs.DeleteKey("CognitoIdToken");
        PlayerPrefs.DeleteKey("CognitoAccessToken");
        PlayerPrefs.DeleteKey("CognitoUsername");
        PlayerPrefs.DeleteKey("CognitoUserId");
        PlayerPrefs.DeleteKey("CognitoRefreshToken");
        PlayerPrefs.Save();
    }

    private void CreateExitButton()
    {
        GameObject canvasObject = new GameObject("ExitOverlayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlayCanvas = canvasObject.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        GameObject buttonObject = new GameObject("SalirButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(overlayCanvas.transform, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(130f, 44f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.78f, 0.08f, 0.08f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.78f, 0.08f, 0.08f, 1f);
        colors.highlightedColor = new Color(0.92f, 0.12f, 0.12f, 1f);
        colors.pressedColor = new Color(0.55f, 0.04f, 0.04f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.onClick.AddListener(QuitGame);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "Salir";
        text.color = Color.white;
        text.fontSize = 24f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    private void ShowIntrusionNotice()
    {
        if (intrusionNoticeGroup == null) CreateIntrusionNoticeText();

        if (intrusionNoticeRoutine != null)
        {
            StopCoroutine(intrusionNoticeRoutine);
        }

        intrusionNoticeRoutine = StartCoroutine(ShowIntrusionNoticeRoutine());
    }

    private void CreateIntrusionNoticeText()
    {
        if (overlayCanvas == null) CreateExitButton();

        GameObject noticeObject = new GameObject("IntrusionNotice", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        noticeObject.transform.SetParent(overlayCanvas.transform, false);

        RectTransform rect = noticeObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -82f);
        rect.sizeDelta = new Vector2(720f, 58f);

        Image background = noticeObject.GetComponent<Image>();
        background.color = new Color(0.95f, 0.18f, 0.08f, 0.9f);

        intrusionNoticeGroup = noticeObject.GetComponent<CanvasGroup>();
        intrusionNoticeGroup.alpha = 0f;
        intrusionNoticeGroup.blocksRaycasts = false;
        intrusionNoticeGroup.interactable = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(noticeObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18f, 0f);
        textRect.offsetMax = new Vector2(-18f, 0f);

        intrusionNoticeText = textObject.GetComponent<TextMeshProUGUI>();
        intrusionNoticeText.text = "Se están intentando meter en tu cuenta.";
        intrusionNoticeText.color = Color.white;
        intrusionNoticeText.fontSize = 28f;
        intrusionNoticeText.fontStyle = FontStyles.Bold;
        intrusionNoticeText.alignment = TextAlignmentOptions.Center;
        intrusionNoticeText.raycastTarget = false;
    }

    private IEnumerator ShowIntrusionNoticeRoutine()
    {
        intrusionNoticeGroup.alpha = 1f;

        yield return new WaitForSeconds(3.5f);

        const float fadeSeconds = 1.5f;
        float elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.deltaTime;
            intrusionNoticeGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeSeconds);
            yield return null;
        }

        intrusionNoticeGroup.alpha = 0f;
    }

    private void OnApplicationQuit()
    {
        // No guardar si la nube aún no respondió: data.score seguiría en 0
        // y machacaría el progreso real.
        if (cloudReady && dbManager != null) dbManager.SaveGameData(data.score, data.timePlayedSeconds);
        SaveToDisk(false);
    }

    private void OnRealJump()
    {
        // Ignora saltos previos a la carga de la nube: no deben puntuar.
        if (!cloudReady) return;

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
        data.timePlayedSeconds += Time.deltaTime;

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
        data.keysPerMinuteLast60 = last60s.Count;
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

    // ===== GUARDADO EN DISCO (FUNCIONA EN BUILD) =====
    public void SaveToDisk(bool saveCloud = true)
    {
        data.userId = GetCurrentUserId();
        data.lastSavedAtUtc = DateTime.UtcNow.ToString("o");

        string dir = GetSavePath();
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string path = GetCurrentLocalSavePath();

        // 1. Datos en bruto
        string rawDataJson = JsonUtility.ToJson(data, prettyPrint: true);

        // 2. Firma HMAC
        string signature = "";
        if (DynamoDBManager.Instance != null)
            signature = DynamoDBManager.Instance.CalculateHMAC(rawDataJson);

        // 3. Sobre seguro (sin nonce ni sessionToken aquí; se añaden al verificar)
        SecurePayload envelope = new SecurePayload
        {
            data      = rawDataJson,
            signature = signature
        };

        // 4. Escritura
        string finalJsonToSave = JsonUtility.ToJson(envelope, prettyPrint: true);
        File.WriteAllText(path, finalJsonToSave);

        Debug.Log($"Telemetría segura guardada en:\n{path}");

        // 5. Guardado en la nube con la misma telemetria que acabamos de firmar.
        if (saveCloud && DynamoDBManager.Instance != null)
        {
            DynamoDBManager.Instance.SaveGameData(data.score, data.timePlayedSeconds);
        }
    }

    public string GetSavePath()
    {
#if UNITY_EDITOR
        string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "session_telemetry"));
#else
        string baseDir = Path.Combine(Application.persistentDataPath, "session_telemetry");
#endif
        return Path.Combine(baseDir, GetSafeUserFolderName());
    }

    private string GetCurrentLocalSavePath()
    {
        return Path.Combine(GetSavePath(), CurrentSaveFileName);
    }

    private string GetCurrentUserId()
    {
        if (dbManager != null)
        {
            string userId = dbManager.GetCurrentPlayerStatsUserId();
            if (!string.IsNullOrEmpty(userId)) return userId;
        }

        string cachedUserId = PlayerPrefs.GetString("CognitoUserId", "");
        if (!string.IsNullOrEmpty(cachedUserId)) return cachedUserId;

        return PlayerPrefs.GetString("CognitoUsername", "UnknownUser");
    }

    private string GetSafeUserFolderName()
    {
        string userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) userId = "UnknownUser";

        char[] chars = userId.ToCharArray();
        char[] invalidChars = Path.GetInvalidFileNameChars();

        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    public SessionTelemetry GetCurrentSnapshot()
    {
        return data;
    }
}
