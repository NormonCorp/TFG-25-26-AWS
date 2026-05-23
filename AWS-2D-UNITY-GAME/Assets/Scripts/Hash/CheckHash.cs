using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class CheckHash : MonoBehaviour
{
    public TMP_Text hashtext;

    public string GetExecutableHash()
    {
#if UNITY_EDITOR
        // Obtiene la ruta del ejecutable actual del Editor.
        string filePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

        using (SHA256 sha256 = SHA256.Create())
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
#else
        // Build: la ruta del binario depende de la plataforma.
        string exePath = null;

#if UNITY_STANDALONE_WIN
        // Windows: <NombreApp>_Data/  ->  <NombreApp>.exe
        exePath = Application.dataPath.Replace("_Data", ".exe");
#elif UNITY_STANDALONE_OSX
        // macOS: el .app es un bundle. Application.dataPath apunta a algo
        // dentro del bundle (.../<App>.app/Contents/Resources/Data normalmente).
        // El binario real está en .../<App>.app/Contents/MacOS/<NombreEjecutable>.
        string appBundle = Application.dataPath;
        while (!string.IsNullOrEmpty(appBundle) && !appBundle.EndsWith(".app"))
        {
            appBundle = Path.GetDirectoryName(appBundle);
        }

        if (!string.IsNullOrEmpty(appBundle))
        {
            string macOSDir = Path.Combine(appBundle, "Contents", "MacOS");
            if (Directory.Exists(macOSDir))
            {
                // Suele haber un único binario; cogemos el primero.
                string[] candidates = Directory.GetFiles(macOSDir);
                if (candidates.Length > 0) exePath = candidates[0];
            }
        }
#elif UNITY_STANDALONE_LINUX
        // Linux: <NombreApp>_Data/  ->  <NombreApp> (sin extensión)
        exePath = Application.dataPath.Replace("_Data", string.Empty);
#endif

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Debug.LogError("No se encuentra el exe: " + exePath);
            return null;
        }

        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(exePath))
        {
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
#endif
    }

    public IEnumerator CheckVersion()
    {
        string url = "https://lb6pg6fy60.execute-api.eu-north-1.amazonaws.com/prod/check-version";
        string clientHash = GetExecutableHash();
        if (hashtext != null) hashtext.text = "hash: " + clientHash;

        // Si no se pudo calcular el hash (binario no encontrado), denegamos.
        if (string.IsNullOrEmpty(clientHash))
        {
            Debug.LogError("No se pudo calcular el hash del binario. Denegando acceso.");
            SetAuthMessage("No se pudo verificar la integridad del juego.");
            SceneManager.LoadScene("LoginScene");
            yield break;
        }

        // Log del hash para diagnóstico: si la Lambda devuelve DENIED, este
        // hash es el que tienes que comparar con la entrada en
        // HistoricExeHashes (clave 'clientHash' en minúsculas).
        Debug.Log("CheckHash: enviando hash = " + clientHash);

        string json = "{\"clientHash\":\"" + clientHash + "\"}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", PlayerPrefs.GetString("CognitoIdToken"));

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
        {
            Debug.Log("Versión validada correctamente.");
        }
        else
        {
            // 403 DENIED, 426 UPDATE_REQUIRED, fallo de red, etc.
            // Cualquier respuesta que no sea 200 OK detiene el juego.
            Debug.LogError("CÓDIGO: " + request.responseCode);
            Debug.LogError("RESPUESTA DE AWS: " + request.downloadHandler.text);
            Debug.LogError("ERROR DE SISTEMA: " + request.error);
            SetAuthMessage(GetLambdaMessage(
                request.downloadHandler.text,
                "Version del juego no autorizada. Descarga la version oficial."
            ));
            SceneManager.LoadScene("LoginScene");
        }
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
            Debug.LogWarning("CheckHash: no se pudo leer el mensaje de Lambda: " + e.Message);
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Comentario para indicar que no se checkea el hash en editor ni devBuilds
        Debug.LogWarning("CheckHash: bypass activo (DEVELOPMENT_BUILD / Editor). " +
                         "El control de version solo se aplica en builds de release.");
#else
        StartCoroutine(CheckVersion());
#endif
    }
}
