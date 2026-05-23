// --- ⚠️ RELLENA ESTO CON TUS DATOS ---
const poolData = {
    UserPoolId: 'eu-north-1_o4xSSQGiK',       // UserPool ID
    ClientId: '35cbbhp6mv494umuihlq2d1u1b'    // App Client ID
};

// Pega aquí la URL que obtuviste en el PASO 3.5 (Debe terminar en /prod)
// Y añádele "/get-game" al final.
const API_URL = 'https://s83rvjyf5h.execute-api.eu-north-1.amazonaws.com/prod/get-game'; 
// -------------------------------------

var userPool = new AmazonCognitoIdentity.CognitoUserPool(poolData);
var cognitoUser;

// Verificar si ya hay sesión al cargar la página
window.onload = function() {
    var currentUser = userPool.getCurrentUser();
    if (currentUser != null) {
        currentUser.getSession(function(err, session) {
            if (err) { return; }
            if (session.isValid()) {
                console.log("Sesión recuperada");
                // Guardar token fresco
                localStorage.setItem('idToken', session.getIdToken().getJwtToken());
                showDownloadSection(currentUser.getUsername());
            }
        });
    }
};

function login() {
    var username = document.getElementById("username").value;
    var authenticationData = {
        Username: username,
        Password: document.getElementById("password").value,
    };

    var authenticationDetails = new AmazonCognitoIdentity.AuthenticationDetails(authenticationData);

    var userData = {
        Username: username,
        Pool: userPool,
    };

    cognitoUser = new AmazonCognitoIdentity.CognitoUser(userData);

    cognitoUser.authenticateUser(authenticationDetails, {
        onSuccess: function(result) {
            console.log("Login exitoso!");
            // El ID Token es el que usamos para autorizar contra API Gateway
            var idToken = result.getIdToken().getJwtToken();
            localStorage.setItem('idToken', idToken);

            showDownloadSection(username);
        },

        onFailure: function(err) {
            console.error(err);
            document.getElementById("error-msg").innerText = "Error: " + (err.message || JSON.stringify(err));
        },
    });
}

function showDownloadSection(username) {
    document.getElementById("login-section").classList.add("hidden");
    document.getElementById("download-section").classList.remove("hidden");
    document.getElementById("user-display").innerText = username;
}

function getDownloadLink() {
    var btn = document.getElementById("btn-download");
    var msg = document.getElementById("status-msg");
    var fileInfo = document.getElementById("file-info");
    
    btn.disabled = true;
    btn.innerText = "⏳ Conectando con servidor...";
    msg.innerText = "";

    var idToken = localStorage.getItem('idToken');

    if (!idToken) {
        alert("Tu sesión ha caducado.");
        logout();
        return;
    }

    // Petición segura a tu API Gateway
    fetch(API_URL, {
        method: 'GET',
        headers: {
            'Authorization': idToken // Aquí va el carnet de identidad
        }
    })
    .then(response => {
        if (response.status === 401) throw new Error("No autorizado. Inicia sesión de nuevo.");
        if (!response.ok) throw new Error("Error en el servidor: " + response.status);
        return response.json();
    })
    .then(data => {
        // La Lambda "Dynamic" que hicimos devuelve: { downloadUrl: "...", fileName: "..." }
        
        if (data.error) throw new Error(data.error);

        console.log("Archivo encontrado:", data.fileName);
        fileInfo.innerText = "Descargando: " + data.fileName;
        
        // Iniciar descarga
        window.location.href = data.downloadUrl;
        
        btn.disabled = false;
        btn.innerText = "DESCARGA INICIADA";
        setTimeout(() => { btn.innerText = "OBTENER JUEGO (.ZIP)"; }, 3000);
    })
    .catch(error => {
        console.error('Error:', error);
        msg.innerText = error.message;
        msg.style.color = "#ff6b6b";
        btn.disabled = true;
        btn.innerText = "Sesión cerrada";

        setTimeout(logout, 2000);
    });
}

function logout() {
    var currentUser = userPool.getCurrentUser();
    if (currentUser) {
        currentUser.signOut();
    }
    localStorage.removeItem('idToken');
    location.reload();
}  