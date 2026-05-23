using System.Collections;
using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class DemoCheck : MonoBehaviour
{
    // Cronómetro para volver a llamar al método, evitando que se juegue
    // con versiones vencidas en sesiones largas.
    private float chrono = 0.0f;
    [SerializeField]
    private float checkTimer = 10.0f;

    private const string DemoCheckUrl = "https://lb6pg6fy60.execute-api.eu-north-1.amazonaws.com/prod/demoCheck";

    IEnumerator CheckAccess()
    {
        UnityWebRequest request = UnityWebRequest.Get(DemoCheckUrl);
        request.SetRequestHeader("Authorization", GetAuthToken());

        yield return request.SendWebRequest();

        // En UnityWebRequest, un 403 también marca result == ProtocolError.
        //   - responseCode 0  -> no hubo respuesta
        //   - responseCode != 0 y != 200 -> Lambda nos rechazó
        //   - responseCode == 200 -> OK
        long status = request.responseCode;

        if (status == 0)
        {
            // Fallo de red real: no echamos al usuario para evitar falsos
            // positivos por wifi malo. El siguiente tick volverá a probar.
            Debug.LogError("Error conexión DemoCheck (sin respuesta): " + request.error);
            yield break;
        }

        if (status != 200)
        {
            Debug.LogError($"Acceso denegado por DemoCheck (HTTP {status}): "
                           + request.downloadHandler.text);
            SetAuthMessage(GetLambdaMessage(
                request.downloadHandler.text,
                "El periodo de demo ha finalizado."
            ));
            PlayerPrefs.DeleteKey("CognitoIdToken");
            PlayerPrefs.DeleteKey("CognitoAccessToken");
            PlayerPrefs.DeleteKey("CognitoRefreshToken");
            PlayerPrefs.Save();
            SceneManager.LoadScene("LoginScene");
            yield break;
        }

        Debug.Log("Acceso permitido");
    }

    private string GetLambdaMessage(string responseText, string fallback)
    {
        if (string.IsNullOrEmpty(responseText)) return fallback;

        try
        {
            LambdaResponse response = JsonUtility.FromJson<LambdaResponse>(responseText);
            if (response != null && !string.IsNullOrEmpty(response.message))
            {
                return response.message;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("DemoCheck: no se pudo leer el mensaje de Lambda: " + e.Message);
        }

        return fallback;
    }

    private void SetAuthMessage(string message)
    {
        PlayerPrefs.SetString("AuthMessage", message);
        PlayerPrefs.Save();
    }

    void Start()
    {
        StartCoroutine(CheckAccess());
    }

    private void Update()
    {
        chrono += Time.deltaTime;
        if (chrono >= checkTimer)
        {
            chrono = 0.0f;
            StartCoroutine(CheckAccess());
        }
    }

    private void OnApplicationQuit()
    {
        UnityWebRequest request = UnityWebRequest.Get(DemoCheckUrl);
        request.SetRequestHeader("Authorization", GetAuthToken());
        request.SendWebRequest();
        Debug.Log("Aplicación cerrada.");
    }

    private string GetAuthToken()
    {
        if (DynamoDBManager.Instance != null)
        {
            string activeToken = DynamoDBManager.Instance.GetCurrentIdToken();
            if (!string.IsNullOrEmpty(activeToken)) return activeToken;
        }

        return PlayerPrefs.GetString("CognitoIdToken");
    }
}
