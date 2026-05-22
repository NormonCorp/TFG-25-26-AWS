using UnityEngine;
using Amazon;
using Amazon.CognitoIdentity;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Unity.VectorGraphics;
using UnityEngine.SceneManagement;
using System.Globalization;

public class DynamoDBManager : MonoBehaviour
{
    public static DynamoDBManager Instance;

    private RegionEndpoint _Region = RegionEndpoint.EUNorth1;
    private const string IdentityPoolId = "eu-north-1:026486dc-0716-49c0-99d2-ac6cb07a8c21";
    private const string UserPoolId = "eu-north-1_o4xSSQGiK";
    private const string TableName = "PlayerStats";

    // Endpoints
    private const string RegisterSessionUrl = "https://s83rvjyf5h.execute-api.eu-north-1.amazonaws.com/prod/register-session";
    private const string VerifyApiUrl       = "https://s83rvjyf5h.execute-api.eu-north-1.amazonaws.com/prod/verify-stats";
    private const string RequestNonceUrl    = "https://s83rvjyf5h.execute-api.eu-north-1.amazonaws.com/prod/request-nonce";

    // ticket sesion actual
    private string _currentSessionToken;
    private IAmazonDynamoDB _ddbClient;
    private CognitoAWSCredentials _credentials;
    private string _publicIP = "Unknown";

    // Ahora se usa nonce, esta clave ahora es prescindible
    public const string SECRET_HMAC_KEY = "MiClaveSecretaAntiCheatTFG";

    // Para cierre limpio sin perder el unregister.
    private bool _isQuitting       = false;
    private bool _unregisterDone   = false;

    void Awake()
    {
        UnityInitializer.AttachToGameObject(this.gameObject);
        AWSConfigs.HttpClient = AWSConfigs.HttpClientOption.UnityWebRequest;

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Bloqueamos el quit hasta haber liberado el slot en AWS.
        Application.wantsToQuit += OnWantsToQuit;
    }

    void Start()
    {
        StartCoroutine(GetPublicIP());
    }

    IEnumerator GetPublicIP()
    {
        UnityWebRequest www = UnityWebRequest.Get("https://api.ipify.org");
        yield return www.SendWebRequest();
        if (www.result == UnityWebRequest.Result.Success) _publicIP = www.downloadHandler.text;
    }


    public void RegisterNewSession(Action onSessionRegistered)
    {
        string idToken = PlayerPrefs.GetString("CognitoIdToken");
        if (string.IsNullOrEmpty(idToken)) return;

        _currentSessionToken = Guid.NewGuid().ToString();

        string jsonPayload = $"{{\"action\":\"register\", \"sessionToken\":\"{_currentSessionToken}\"}}";
        StartCoroutine(SendRegisterSession(jsonPayload, idToken, onSessionRegistered));
    }

    private IEnumerator SendRegisterSession(string jsonPayload, string token, Action onSuccess)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
        UnityWebRequest request = new UnityWebRequest(RegisterSessionUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", token);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            LambdaResponse response = JsonUtility.FromJson<LambdaResponse>(request.downloadHandler.text);

            if (response.code == "SESSION_IN_USE")
            {
                Debug.LogError("ACCESO DENEGADO: Tu cuenta ya está jugando en otro dispositivo.");
                _currentSessionToken = null;
                PlayerPrefs.DeleteKey("CognitoIdToken");
                SceneManager.LoadScene("LoginScene");
                yield break;
            }
            else if (response.code == "PERMANENT_BANNED")
            {
                Debug.LogError("Cuenta baneada permanentemente.");
                _currentSessionToken = null;
                SignOutAWS();
                PlayerPrefs.DeleteKey("CognitoIdToken");
                PlayerPrefs.DeleteKey("CognitoAccessToken");
                PlayerPrefs.DeleteKey("CognitoRefreshToken");
                PlayerPrefs.SetString("AuthMessage", "Cuenta baneada permanentemente.");
                SceneManager.LoadScene("LoginScene");
                yield break;
            }
            else if (response.code == "OK")
            {
                Debug.Log("Tienes el control de la cuenta.");
                onSuccess?.Invoke();
            }
        }
        else Debug.LogError("Error en registro de sesión: " + request.error);
    }


    // CAMBIO: ahora usa UpdateItem en vez de PutItem para no machacar
    // SessionLastSeenAt / SessionStartedAt. La condicion sigue garantizando
    // que solo el dueño del slot puede escribir (ActiveSessionToken == el mio).
    public void SaveGameData(int score, float timePlayed)
    {
        string idToken  = PlayerPrefs.GetString("CognitoIdToken");
        string userId   = GetPlayerStatsUserId(idToken);

        if (string.IsNullOrEmpty(idToken)) return;
        if (_ddbClient == null) InitClient(idToken);
        if (string.IsNullOrEmpty(_currentSessionToken)) return;

        var request = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "UserId", new AttributeValue { S = userId } }
            },
            UpdateExpression =
                "SET Score = :s, TimePlayed = :t, IPAddress = :ip, LastUpdated = :lu, SessionLastSeenAt = :now",
            ConditionExpression =
                "ActiveSessionToken = :myToken",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":s",       new AttributeValue { N = score.ToString(CultureInfo.InvariantCulture) } },
                { ":t",       new AttributeValue { N = timePlayed.ToString("F2", CultureInfo.InvariantCulture) } },
                { ":ip",      new AttributeValue { S = _publicIP } },
                { ":lu",      new AttributeValue { S = DateTime.UtcNow.ToString("o") } },
                { ":now",     new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) } },
                { ":myToken", new AttributeValue { S = _currentSessionToken } }
            }
        };

        _ddbClient.UpdateItemAsync(request, (result) =>
        {
            if (result.Exception != null)
            {
                if (result.Exception.Message.Contains("ConditionalCheckFailed") || result.Exception is ConditionalCheckFailedException)
                {
                    Debug.LogError("SESIÓN CADUCADA DURANTE EL GUARDADO.");
                    _currentSessionToken = null;
                    PlayerPrefs.DeleteKey("CognitoIdToken");
                    SceneManager.LoadScene("LoginScene");
                }
                else
                {
                    Debug.LogError("Error guardando: " + result.Exception.Message);
                }
            }
            else
            {
                Debug.Log("Guardado validado en la nube.");
            }
        });
    }

    public void LoadData(Action<int, float> onLoadedCallback)
    {
        string idToken  = PlayerPrefs.GetString("CognitoIdToken");
        string userId   = GetPlayerStatsUserId(idToken);

        if (string.IsNullOrEmpty(idToken)) return;
        if (_ddbClient == null) InitClient(idToken);

        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue> { { "UserId", new AttributeValue { S = userId } } }
        };

        _ddbClient.GetItemAsync(request, (result) =>
        {
            if (result.Exception != null)
            {
                Debug.LogError("Error cargando DynamoDB: " + result.Exception.Message);
                return;
            }

            if (result.Response != null && result.Response.Item != null && result.Response.Item.Count > 0)
            {
                var item = result.Response.Item;
                int loadedScore = 0;
                float loadedTime = 0f;


                if (item.ContainsKey("Score"))      int.TryParse(item["Score"].N, out loadedScore);
                if (item.ContainsKey("TimePlayed")) float.TryParse(item["TimePlayed"].N, NumberStyles.Any, CultureInfo.InvariantCulture, out loadedTime);

                Debug.Log($"DATOS RECIBIDOS: Score {loadedScore} | Tiempo {loadedTime}");
                onLoadedCallback?.Invoke(loadedScore, loadedTime);
            }
            else
            {
                Debug.Log("Usuario nuevo: no hay datos persistidos en DynamoDB.");
                onLoadedCallback?.Invoke(0, 0f);
            }
        });
    }

    public string GetCurrentPlayerStatsUserId()
    {
        string idToken = PlayerPrefs.GetString("CognitoIdToken");
        return GetPlayerStatsUserId(idToken);
    }

    private string GetPlayerStatsUserId(string idToken)
    {
        string userId = PlayerPrefs.GetString("CognitoUserId", "");
        if (!string.IsNullOrEmpty(userId)) return userId;

        userId = CognitoTokenUtils.GetCognitoUsername(idToken);
        if (!string.IsNullOrEmpty(userId))
        {
            PlayerPrefs.SetString("CognitoUserId", userId);
            PlayerPrefs.Save();
            return userId;
        }

        return PlayerPrefs.GetString("CognitoUsername", "UnknownUser");
    }

    public void SignOutAWS()
    {
        if (_credentials != null)
        {
            _credentials.Clear();
            _credentials = null;
        }
        _ddbClient = null;
        Debug.Log("AWS Credentials limpiadas.");
    }

    private void InitClient(string idToken)
    {
        _credentials = new CognitoAWSCredentials(IdentityPoolId, _Region);
        _credentials.Clear();
        _credentials.AddLogin("cognito-idp.eu-north-1.amazonaws.com/" + UserPoolId, idToken);
        _ddbClient = new AmazonDynamoDBClient(_credentials, _Region);
    }


    private IEnumerator RequestNonce(string token, Action<string> onNonceReady)
    {
        UnityWebRequest request = new UnityWebRequest(RequestNonceUrl, "POST");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", token);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            NonceResponse resp = JsonUtility.FromJson<NonceResponse>(request.downloadHandler.text);
            if (resp != null && !string.IsNullOrEmpty(resp.nonce))
            {
                onNonceReady?.Invoke(resp.nonce);
                yield break;
            }
            Debug.LogError("Nonce vacío en la respuesta: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("Error pidiendo nonce: " + request.error + " | " + request.downloadHandler.text);
        }

        onNonceReady?.Invoke(null);
    }

    public void VerifyDataAtStartup(string envelopeJson, Action onVerificationDone, Action<int, float> onForceCloud = null)
    {
        string idToken = PlayerPrefs.GetString("CognitoIdToken");
        if (string.IsNullOrEmpty(idToken))
        {
            onVerificationDone?.Invoke();
            return;
        }

        // Paso 1: pedir nonce
        StartCoroutine(RequestNonce(idToken, (nonce) =>
        {
            if (string.IsNullOrEmpty(nonce))
            {
                Debug.LogError("No se obtuvo nonce. Saltando verificación.");
                onVerificationDone?.Invoke();
                return;
            }

            // Paso 2: meter nonce + sessionToken en el sobre y enviar verify
            bool emptyEnvelope = string.IsNullOrEmpty(envelopeJson) || envelopeJson.Trim().Length == 0;
            SecurePayload payload = emptyEnvelope
                ? new SecurePayload()
                : JsonUtility.FromJson<SecurePayload>(envelopeJson);
            if (payload == null) payload = new SecurePayload();
            if (payload.data == null) payload.data = "";
            if (payload.signature == null) payload.signature = "";
            payload.sessionToken  = _currentSessionToken;
            payload.nonce         = nonce;

            string finalPayload = JsonUtility.ToJson(payload);
            StartCoroutine(SendVerifyRequest(finalPayload, idToken, onVerificationDone, onForceCloud));
        }));
    }

    private IEnumerator SendVerifyRequest(string jsonPayload, string token, Action onVerificationDone, Action<int, float> onForceCloud)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        UnityWebRequest request = new UnityWebRequest(VerifyApiUrl, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", token);

        Debug.Log("Enviando JSON local a AWS para verificación...");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseJson = request.downloadHandler.text;
            Debug.Log("Veredicto AWS: " + responseJson);

            LambdaResponse response = JsonUtility.FromJson<LambdaResponse>(responseJson);
            if (response != null)
            {
                if (response.code == "SESSION_EXPIRED")
                {
                    Debug.LogError("SESIÓN CADUCADA.");
                    _currentSessionToken = null;
                    PlayerPrefs.DeleteKey("CognitoIdToken");
                    PlayerPrefs.DeleteKey("CognitoAccessToken");
                    PlayerPrefs.DeleteKey("CognitoRefreshToken");
                    SceneManager.LoadScene("LoginScene");
                    yield break;
                }
                else if (response.code == "PERMANENT_BANNED")
                {
                    Debug.LogError("Cuenta baneada permanentemente.");
                    _currentSessionToken = null;
                    SignOutAWS();
                    PlayerPrefs.DeleteKey("CognitoIdToken");
                    PlayerPrefs.DeleteKey("CognitoAccessToken");
                    PlayerPrefs.DeleteKey("CognitoRefreshToken");
                    PlayerPrefs.SetString("AuthMessage", "Cuenta baneada permanentemente.");
                    SceneManager.LoadScene("LoginScene");
                    yield break;
                }
                else if (response.code == "CHEAT_WARNING")    Debug.LogError("Aviso de trampa: stats reseteadas.");
                else if (response.code == "FORCE_CLOUD")
                {
                    Debug.Log($"Archivo local ausente/desactualizado. Usando nube: Score {response.cloudScore} | Tiempo {response.cloudTime}");
                    onForceCloud?.Invoke(response.cloudScore, response.cloudTime);
                }
                else if (response.code == "NONCE_INVALID")    Debug.LogError("Nonce inválido o caducado.");
                else                                          Debug.Log("Datos verificados.");
            }
        }
        else
        {
            Debug.LogError("Error conectando con verify: " + request.error + " | " + request.downloadHandler.text);
        }

        onVerificationDone?.Invoke();
    }

    public string CalculateHMAC(string text)
    {
        byte[] keyBytes  = Encoding.UTF8.GetBytes(SECRET_HMAC_KEY);
        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        using (HMACSHA256 hmac = new HMACSHA256(keyBytes))
        {
            byte[] hashBytes = hmac.ComputeHash(textBytes);
            return Convert.ToBase64String(hashBytes);
        }
    }

    // CAMBIO: el unregister al cerrar ahora viaja con sessionToken, asi
    // la Lambda solo libera el slot si realmente eres su dueño. Ademas
    // bloqueamos el quit hasta que el request termine, para no perderlo.
    private bool OnWantsToQuit()
    {
        if (_unregisterDone) return true;

        string idToken = PlayerPrefs.GetString("CognitoIdToken");
        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(_currentSessionToken))
        {
            // Nada que liberar.
            return true;
        }

        if (!_isQuitting)
        {
            _isQuitting = true;
            StartCoroutine(UnregisterAndQuit(idToken, _currentSessionToken));
        }
        return false; // Aborta el quit; lo relanzamos cuando termine.
    }

    private IEnumerator UnregisterAndQuit(string idToken, string sessionToken)
    {
        Debug.Log("Liberando la cuenta en AWS...");

        string jsonPayload = $"{{\"action\":\"unregister\",\"sessionToken\":\"{sessionToken}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        UnityWebRequest request = new UnityWebRequest(RegisterSessionUrl, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", idToken);
        request.timeout = 5; // no bloquees el cierre indefinidamente

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("Unregister no confirmado: " + request.error +
                             " (la red de seguridad stale-takeover liberará el slot).");
        }
        else
        {
            Debug.Log("Slot liberado correctamente.");
        }

        _unregisterDone = true;
        Application.Quit();
    }
}

[Serializable]
public class LambdaResponse
{
    public string code;
    public string message;
    public int cloudScore;
    public float cloudTime;
}

[Serializable]
public class NonceResponse
{
    public string code;
    public string message;
    public string nonce;
}

[Serializable]
public class SecurePayload
{
    public string data;          // JSON de session_telemetry
    public string signature;     // HMAC del data
    public string sessionToken;  // Ticket de sesion
    public string nonce;         // nonce de un solo uso
}
