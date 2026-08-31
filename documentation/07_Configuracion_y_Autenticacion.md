# Configuración y Autenticación NTLM

Para que este proyecto logre ser ejecutado transparentemente en una máquina corporativa dentro de la intranet, cuenta con un robusto sistema dual de persistencia y maneja negociaciones criptográficas de autenticación de Windows sin solicitar credenciales por pantalla.

## 1. Sistema Dual de Almacenamiento de Configuraciones
La aplicación nunca deja de funcionar si pierde el archivo base. Las configuraciones (URL, DB, API de Power BI) viven en dos mundos sincrónicamente ligados por `ConfigurationSyncService`:

- **El archivo `.env` o `appsettings.json`**: Fuente en formato texto en disco, ideal para despliegues iniciales y Docker.
- **Base de Datos (`staging.system_configuration`)**: Almacena en PostgreSQL un string en JSON (`config_value`).
- **Flujo de Resiliencia**: Cuando la aplicación de escritorio se abre y el usuario guarda cambios en la ventana de *Configuración*, la aplicación guarda estos datos en la base de datos e invalida la cache (`IOptionsMonitor`). El orquestador de fondo detecta el cambio e inyecta la nueva configuración *hot-reload* sin necesidad de reiniciar la app. Si se borra la BD, se vuelve a rellenar (seed) a partir del `.env` local.

## 2. Autenticación Integrada de Windows (SSPI / NTLM / Kerberos)
Dado que TFS On-Premise está enlazado a un dominio de Active Directory, es vital que la aplicación acceda al servidor haciéndose pasar por el portador/usuario.

Se configuró el pipeline HTTP de .NET usando una factoría especializada (`NtlmHttpHandlerFactory`):
1. **SSO Transparente (`UseDefaultCredentials = true`)**: El handler le pide al proceso de sistema (LSASS.exe en Windows) un token criptográfico generado desde la sesión viva (quien hizo Login en la laptop).
2. **Negociación Automática**: Cuando `HttpClient` golpea `/_apis/projects`, TFS rechaza con `401 Unauthorized` pero devuelve la cabecera `WWW-Authenticate: NTLM`. .NET intercepta el rechazo y responde mandando el token encriptado (handshake), dándole acceso automático.

### ¿Por qué esto es fundamental en corporativos?
- **No se guardan contraseñas**: La app nunca teclea contraseñas en texto claro, previniendo riesgos de seguridad.
- **Sobrevive a expiraciones**: Las políticas corporativas obligan a cambiar claves cada 90 días. Puesto que la app se vale de los "tickets" vivos de sesión actual del portador, no se rompe la sincronización.
- **Acceso Legítimo**: La App de escritorio solo sincronizará los proyectos y *Work items* en los que el usuario activo explícitamente tenga visibilidad en Azure DevOps, cumpliendo con los estándares de seguridad de Recursos Humanos.
- **Renovación de Handler**: La inyección de dependencias (DI) usa `SetHandlerLifetime(TimeSpan.FromMinutes(15))` para reciclar periódicamente las conexiones DNS y el socket.

## 3. Power BI Service (OAuth2)
La configuración del refresco de los tableros online (`PowerBiRefreshService`) opera con un flujo de autenticación "Service Principal" (Confidential Client App) de Microsoft Authentication Library (MSAL). Utilizando `ConfidentialClientApplicationBuilder`, adquiere el token (mediante `AcquireTokenForClient`) con el `Tenant ID`, `Client ID` y `Client Secret` parametrizados, manteniéndose totalmente agnóstica y al margen de los tokens NTLM del TFS.
