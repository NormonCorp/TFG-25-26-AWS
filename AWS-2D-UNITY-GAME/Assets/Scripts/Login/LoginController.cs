using System.Collections.Generic;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Security.Cryptography;
using TMPro;
using UnityEngine.SceneManagement;

/* USER POOL 
https://eu-north-1.console.aws.amazon.com/cognito/v2/idp/user-pools/eu-north-1_Zi37vCBqh/overview?region=eu-north-1
VIDEO Y WEB DE REFERENCIA
https://www.youtube.com/watch?v=dNqzWIbHFAQ https://medium.com/better-programming/building-a-simple-signup-flow-with-unity3d-and-aws-cognito-e0b28e4c342d
*/


public class LoginController : MonoBehaviour
{
    [SerializeField]
    private bool token = false;
    // ID del app client del user pool
    const string CLIENTID = "4rka5dogcs4k6k2v1epfnkc87j";

    // nombre del username, tiene que ser el correo al que llegara el codigo de verificacion
    string USERNAME;

    // client secret, en apartado app clients
    const string HASH = "1thmvsd85jv4573jak4h125tlhbbe9smv1qnav5bl76864892a0v";
    
    // codigo de verificacion que llega al correo
    string CODE;

    // preferred_username
    string NICKNAME;

    // contraseña
    string PASSWORD;

    [SerializeField]
    private string goToScene;
    [SerializeField]
    private TMP_Text errorText;

    [System.Serializable]
    public class AuthenticationResult
    {
        public string IdToken;
        public string AccessToken;
        public string RefreshToken;
    }

    [System.Serializable]
    public class LoginResponse
    {
        public AuthenticationResult AuthenticationResult;
    }

    [System.Serializable]
    public class SignUpAttribute
    {
        public string Name;
        public string Value;
    }

    [System.Serializable]
    public class SignUpSendData
    {
        public string Username;
        public string Email;
        public string Password;
        public string ClientId;
        public string SecretHash;
        public List<SignUpAttribute> UserAttributes;
    }

    [System.Serializable]
    public class ConfirmSignUpSendData
    {
        public string Email;
        public string Username;
        public string ConfirmationCode;
        public string ClientId;
        public string SecretHash;
    }

    [System.Serializable]
    public class AuthParameters
    {
        public string USERNAME;
        public string PASSWORD;
        public string SECRET_HASH;
    }

    [System.Serializable]
    public class LoginSendData
    {
        public string AuthFlow = "USER_PASSWORD_AUTH";
        public string ClientId;
        public AuthParameters AuthParameters;
    }

    // CORRUTINA PARA REGISTRAR AL USUARIO SIN VERIFICAR EN COGNITO
    IEnumerator SignUp()
    {
        SignUpSendData sendData = new SignUpSendData();
        sendData.Username = NICKNAME;
        sendData.Email = USERNAME;
        sendData.Password = PASSWORD; 
        sendData.ClientId = CLIENTID;
        sendData.UserAttributes = new List<SignUpAttribute>();

        sendData.UserAttributes.Add(new SignUpAttribute
        {
            Name = "email",
            Value = USERNAME
        });

        sendData.UserAttributes.Add(new SignUpAttribute
        {
            Name = "preferred_username",
            Value = NICKNAME 
        });

        string clientSecret = HASH;
        sendData.SecretHash = CalculateSecretHash(clientSecret, sendData.Username, sendData.ClientId);

        string jsonPayload = JsonUtility.ToJson(sendData);
        byte[] bytePostData = Encoding.UTF8.GetBytes(jsonPayload);

        UnityWebRequest request = new UnityWebRequest("https://cognito-idp.eu-north-1.amazonaws.com/", "POST");
        request.uploadHandler = new UploadHandlerRaw(bytePostData);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.1");
        request.SetRequestHeader("X-Amz-Target", "AWSCognitoIdentityProviderService.SignUp");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Success: " + request.downloadHandler.text);
            if (errorText != null) errorText.text = "Success";
        }
        else
        {
            Debug.LogError("Error " + request.responseCode + ": " + request.downloadHandler.text);
            if (errorText != null) errorText.text = "Error en Login " + request.responseCode + ": " + request.downloadHandler.text;
        }
    }

    // CORRUTINA PARA VERIFICAR CON UN CODIGO DE VERIFICACION A UN USUARIO YA REGISTRADO
    IEnumerator ConfirmSignUp()
    {
        ConfirmSignUpSendData sendData = new ConfirmSignUpSendData();
        sendData.Email = USERNAME;
        sendData.ConfirmationCode = CODE;
        sendData.ClientId = CLIENTID;
        sendData.Username = NICKNAME;
        string clientSecret = HASH;
        sendData.SecretHash = CalculateSecretHash(clientSecret, sendData.Username, sendData.ClientId);

        byte[] bytePostData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(sendData));
        UnityWebRequest request = UnityWebRequest.Put("https://cognito-idp.eu-north-1.amazonaws.com/", bytePostData);
        request.method = "POST";
        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.1; charset=UTF-8");
        request.SetRequestHeader("X-Amz-Target", "AWSCognitoIdentityProviderService.ConfirmSignUp");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Success: " + request.downloadHandler.text);
            if (!string.IsNullOrEmpty(goToScene))
            {
                SceneManager.LoadScene(goToScene);
            }
        }
        else
        {
            Debug.LogError("Error " + request.responseCode + ": " + request.downloadHandler.text);
            if (errorText != null) errorText.text = "Error en Login " + request.responseCode + ": " + request.downloadHandler.text;
        }
    }

    // CORRUTINA PARA INICIAR SESION, TIENE QUE EXISTIR EL USUARIO
    IEnumerator SignIn()
    {
        if (DynamoDBManager.Instance != null)
        {
            DynamoDBManager.Instance.SignOutAWS();
        }

        LoginSendData sendData = new LoginSendData();
        sendData.ClientId = CLIENTID;
        sendData.AuthParameters = new AuthParameters
        {
            USERNAME = USERNAME,
            PASSWORD = PASSWORD,
            SECRET_HASH = CalculateSecretHash(HASH, USERNAME, CLIENTID)
        };

        string jsonPayload = JsonUtility.ToJson(sendData);
        byte[] bytePostData = Encoding.UTF8.GetBytes(jsonPayload);

        UnityWebRequest request = new UnityWebRequest("https://cognito-idp.eu-north-1.amazonaws.com/", "POST");
        request.uploadHandler = new UploadHandlerRaw(bytePostData);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.1");
        request.SetRequestHeader("X-Amz-Target", "AWSCognitoIdentityProviderService.InitiateAuth");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            // No logueamos el cuerpo de la respuesta porque contiene IdToken/AccessToken/RefreshToken.
            Debug.Log("Login Exitoso.");

            // Extracción de tokens del JSON de respuesta
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);

            string cognitoUserId = CognitoTokenUtils.GetCognitoUsername(response.AuthenticationResult.IdToken);
            if (string.IsNullOrEmpty(cognitoUserId)) cognitoUserId = USERNAME;

            // Guardamos el IdToken, AccessToken, Username y RefreshToken en PlayerPrefs (Persistencia)
            PlayerPrefs.SetString("CognitoIdToken", response.AuthenticationResult.IdToken);
            PlayerPrefs.SetString("CognitoAccessToken", response.AuthenticationResult.AccessToken);
            PlayerPrefs.SetString("CognitoUsername", USERNAME);
            PlayerPrefs.SetString("CognitoUserId", cognitoUserId);
            PlayerPrefs.SetString("CognitoRefreshToken", response.AuthenticationResult.RefreshToken);
            PlayerPrefs.Save();

            if (!string.IsNullOrEmpty(goToScene))
            {
                SceneManager.LoadScene(goToScene);
            }
        }
        else
        {
            Debug.LogError("Error en Login " + request.responseCode + ": " + request.downloadHandler.text);
            if (errorText != null)
            {
                string raw = request.downloadHandler.text;
                if (raw != null && raw.Contains("disabled"))
                    errorText.text = "Cuenta baneada permanentemente.";
                else
                    errorText.text = "Error en Login: usuario o contraseña incorrectos.";
            }
        }
    }

    // CORRUTINA PARA REFRESCAR LA SESIÓN
    IEnumerator RefreshSession()
    {
        // Recuperamos los datos guardados
        string savedUsername = PlayerPrefs.GetString("CognitoUsername");
        string refreshToken = PlayerPrefs.GetString("CognitoRefreshToken");

        LoginSendData sendData = new LoginSendData();
        sendData.AuthFlow = "REFRESH_TOKEN_AUTH"; // Flujo especial para renovar
        sendData.ClientId = CLIENTID;
        sendData.AuthParameters = new AuthParameters
        {
            // IMPORTANTE: Para el refresh, el campo se llama REFRESH_TOKEN
            // Usaremos una clase auxiliar si el JSON falla, pero Cognito suele aceptar esto:
            USERNAME = savedUsername,
            SECRET_HASH = CalculateSecretHash(HASH, savedUsername, CLIENTID)
        };

        // El Refresh Token se envía fuera de AuthParameters en algunos flujos o dentro según versión
        // Para USER_PASSWORD_AUTH con Refresh:
        string json = "{\"AuthFlow\":\"REFRESH_TOKEN_AUTH\",\"ClientId\":\"" + CLIENTID + "\",\"AuthParameters\":{\"REFRESH_TOKEN\":\"" + refreshToken + "\",\"SECRET_HASH\":\"" + sendData.AuthParameters.SECRET_HASH + "\"}}";

        byte[] bytePostData = Encoding.UTF8.GetBytes(json);
        UnityWebRequest request = new UnityWebRequest("https://cognito-idp.eu-north-1.amazonaws.com", "POST");
        request.uploadHandler = new UploadHandlerRaw(bytePostData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/x-amz-json-1.1");
        request.SetRequestHeader("X-Amz-Target", "AWSCognitoIdentityProviderService.InitiateAuth");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
            PlayerPrefs.SetString("CognitoIdToken", response.AuthenticationResult.IdToken);
            string cognitoUserId = CognitoTokenUtils.GetCognitoUsername(response.AuthenticationResult.IdToken);
            if (!string.IsNullOrEmpty(cognitoUserId)) PlayerPrefs.SetString("CognitoUserId", cognitoUserId);
            PlayerPrefs.Save();
            Debug.Log("Token renovado con Exito.");
        }
        else
        {
            Debug.LogError("Error renovando token. El usuario debe loguearse otra vez.");
            Logout();
        }
    }

    // CALCULO DE HASH
    string CalculateSecretHash(string userPoolClientSecret, string userName, string userPoolClientId)
    {
        string message = userName + userPoolClientId;
        byte[] keyBytes = Encoding.UTF8.GetBytes(userPoolClientSecret);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        using (HMACSHA256 hmac = new HMACSHA256(keyBytes))
        {
            byte[] hashBytes = hmac.ComputeHash(messageBytes);
            return System.Convert.ToBase64String(hashBytes);
        }
    }

    // Método para cerrar sesión manualmente
    public void Logout()
    {
        StartCoroutine(LogoutRoutine());
    }

    private IEnumerator LogoutRoutine()
    {
        if (DynamoDBManager.Instance != null)
        {
            bool unregisterFinished = false;
            DynamoDBManager.Instance.UnregisterCurrentSession(() => unregisterFinished = true);
            yield return new WaitUntil(() => unregisterFinished);
            DynamoDBManager.Instance.SignOutAWS();
        }

        PlayerPrefs.DeleteKey("CognitoIdToken");
        PlayerPrefs.DeleteKey("CognitoAccessToken");
        PlayerPrefs.DeleteKey("CognitoUsername");
        PlayerPrefs.DeleteKey("CognitoUserId");
        PlayerPrefs.DeleteKey("CognitoRefreshToken");
        PlayerPrefs.Save();

        Debug.Log("Sesión cerrada completamente.");
        SceneManager.LoadScene("LoginScene");
    }

    // METODOS PARA PASAR TEXTOS DESDE INPUT FIELDS
    // Importante: no logueamos los valores. La contraseña NUNCA debe ir a Player.log,
    // y username/nickname/code tampoco aportan nada en producción.
    public void SetPassword(TMP_InputField pass)
    {
        PASSWORD = pass.text;
    }
    public void SetUsername(TMP_InputField email)
    {
        USERNAME = email.text;
    }
    public void SetNickname(TMP_InputField nick)
    {
        NICKNAME = nick.text;
    }
    public void SetCode(TMP_InputField verCode)
    {
        CODE = verCode.text;
    }



    // METODOS PARA INICIAR LAS CORRUTINAS
    public void SendCode()
    {
        StartCoroutine(SignUp());
    }
    public void LogIn()
    {
        StartCoroutine(SignIn());
    }
    public void ConfSignUp()
    {
        StartCoroutine(ConfirmSignUp());
    }

    void Start()
    {
       //PlayerPrefs.DeleteAll();

        // Mensaje dejado por el kick (p.ej. ban) antes de volver al login.
        string authMsg = PlayerPrefs.GetString("AuthMessage", "");
        if (!string.IsNullOrEmpty(authMsg))
        {
            if (errorText != null) errorText.text = authMsg;
            PlayerPrefs.DeleteKey("AuthMessage");
            PlayerPrefs.Save();
        }

        if (token)
        {
            // Al iniciar, si ya existe un token, saltamos el login directamente
            if (PlayerPrefs.HasKey("CognitoIdToken"))
            {
                errorText.text = "Automatic Login";
                StartCoroutine(RefreshSession());
                if (!string.IsNullOrEmpty(goToScene)) SceneManager.LoadScene(goToScene);
            }
        }
        
    }
}
