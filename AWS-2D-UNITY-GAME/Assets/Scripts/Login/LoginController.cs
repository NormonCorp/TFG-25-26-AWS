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
    // ID del app client del user pool
    const string CLIENTID = "657u1sl26h05ungaonv258qq13";

    // nombre del username, tiene que ser el correo al que llegara el codigo de verificacion
    string USERNAME;

    // client secret, en apartado app clients
    const string HASH = "mp2tmd0pjjog60gu9bpbqp4tdva27sfsap9m859mmdijdpkijpn";
    
    // codigo de verificacion que llega al correo
    string CODE;

    // preferred_username
    string NICKNAME = "user_test_123";

    // contraseña
    string PASSWORD;

    [SerializeField]
    private string goToScene;
    [SerializeField]
    private TMP_Text errorText;

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
        public string Password;
        public string ClientId;
        public string SecretHash;
        public List<SignUpAttribute> UserAttributes;
    }

    [System.Serializable]
    public class ConfirmSignUpSendData
    {
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
        sendData.Username = USERNAME;
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
        sendData.Username = USERNAME;
        sendData.ConfirmationCode = CODE;
        sendData.ClientId = CLIENTID;

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
            Debug.Log("Login Exitoso: " + request.downloadHandler.text);
            // Recibo el IdToken y AccessToken. TO DO guardarlos.
            if (!string.IsNullOrEmpty(goToScene))
            {
                SceneManager.LoadScene(goToScene);
            }
        }
        else
        {
            Debug.LogError("Error en Login " + request.responseCode + ": " + request.downloadHandler.text);
            if (errorText != null) errorText.text = "Error en Login " + request.responseCode + ": " + request.downloadHandler.text;
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

    // METODOS PARA PASAR TEXTOS DESDE INPUT FIELDS
    public void SetPassword(TMP_InputField pass)
    {
        PASSWORD = pass.text;
        Debug.Log(PASSWORD);
    }
    public void SetUsername(TMP_InputField email)
    {
        USERNAME = email.text;
        Debug.Log(USERNAME);
    }
    public void SetCode(TMP_InputField verCode)
    {
        CODE = verCode.text;
        Debug.Log(CODE);
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
}