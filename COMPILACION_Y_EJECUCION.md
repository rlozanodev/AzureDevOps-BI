# Guía de Compilación y Ejecución: Azure DevOps BI Manager

Esta guía detalla los comandos exactos, paso a paso, para preparar el entorno, compilar la solución y ejecutar la nueva aplicación de escritorio nativa (Avalonia UI) para la extracción y orquestación de datos de Azure DevOps.

---

## 1. Pre-requisitos
Asegúrate de contar con lo siguiente en tu sistema:
- **.NET SDK 8.0 o 9.0**: Requerido para compilar y ejecutar el Desktop Manager y el Ingestion Worker.
- **Docker & Docker Compose**: Requerido para levantar la base de datos PostgreSQL donde reside el catálogo.

---

## 2. Inicializar la Base de Datos Local

Antes de correr la aplicación, el Worker y la GUI asumen que la base de datos PostgreSQL está activa (en base al esquema que definimos con `init.sql`). 

1. Abre una terminal en la raíz del proyecto.
2. Ejecuta el siguiente comando para levantar los servicios de Docker (PostgreSQL):
   ```bash
   docker compose up -d
   ```
3. *(Opcional)* Puedes comprobar que el contenedor está corriendo y saludable usando:
   ```bash
   docker ps
   ```

---

## 3. Restaurar y Compilar la Solución

Puedes asegurarte de que todas las dependencias y proyectos estén correctamente vinculados y sin errores compilando la solución principal completa.

```bash
dotnet build AzureDevOps-BI.slnx -c Debug
```
*Si esto muestra "0 Error(s)", significa que todos los paquetes (Avalonia, Dapper, PostgreSQL, etc.) se resolvieron exitosamente.*

---

## 4. Ejecución de la Aplicación (Modo Desarrollo / Pruebas)

Si deseas probar la aplicación de escritorio en vivo (con los logs en consola y viendo la interfaz rápidamente), puedes ejecutar el proyecto directamente:

```bash
dotnet run --project src/AzureDevOps.DesktopManager/AzureDevOps.DesktopManager.csproj
```

**¿Qué esperar?**
- Aparecerá la ventana `Azure DevOps BI Manager` con las 4 pestañas (Dashboard, Catálogo, Configuración y Logs).
- En tu barra de tareas (System Tray) aparecerá un icono de Avalonia que te permitirá restaurar la aplicación si la cierras.

---

## 5. Compilación y Publicación Auto-Contenida (Producción)

Si tu objetivo es generar el binario `.exe` (o binario en Linux/Mac) para el usuario final (el "portador"), de tal forma que **no requiera tener .NET instalado en su máquina**, ejecuta el siguiente comando:

**Para Windows (x64):**
```bash
dotnet publish src/AzureDevOps.DesktopManager/AzureDevOps.DesktopManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Para Linux (x64):**
```bash
dotnet publish src/AzureDevOps.DesktopManager/AzureDevOps.DesktopManager.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

**Parámetros utilizados:**
- `-c Release`: Compila el código optimizado para producción.
- `-r <RID>`: Define el sistema operativo y arquitectura objetivo (`win-x64`, `linux-x64`, `osx-x64`).
- `--self-contained true`: Empaqueta el runtime de .NET junto con la aplicación.
- `-p:PublishSingleFile=true`: Genera un único archivo ejecutable (`.exe` en Windows) en lugar de cientos de archivos `.dll`.

### ¿Dónde encuentro el archivo final?
Tras ejecutar el comando anterior, el binario compilado estará ubicado en la siguiente ruta (ejemplo para Windows):
```
src/AzureDevOps.DesktopManager/bin/Release/net8.0/win-x64/publish/AzureDevOps.DesktopManager.exe
```

Simplemente haz doble clic sobre el ejecutable generado o ejecútalo desde tu terminal:
```bash
./src/AzureDevOps.DesktopManager/bin/Release/net8.0/win-x64/publish/AzureDevOps.DesktopManager
```

---

## 6. Primeros pasos dentro de la Interfaz

1. **Configuración**: Dirígete a la pestaña **Configuración**. Ingresa la `Base URL` (ej. `https://dev.azure.com/mi-organizacion`), el nombre de tu `Collection` (ej. `DefaultCollection`) y tu **Personal Access Token (PAT)**.
2. **Auto-Descubrimiento**: Al conectarse exitosamente (y correr el ciclo interno en background), dirígete a la pestaña **Catálogo (Proyectos)**. Verás la lista de proyectos descubiertos que se han descargado e insertado en PostgreSQL.
3. Puedes deshabilitar los proyectos que no quieras analizar apagando el _switch_ "Enabled", y presionar **Guardar Cambios** para persistir esto en tu catálogo.

---

## 7. Sandbox CLI (Depuración de Autenticación NTLM)

El proyecto **AzureDevOps.SandboxCLI** es una herramienta de consola independiente creada específicamente para aislar y depurar problemas de autenticación NTLM contra servidores locales de Azure DevOps (TFS). 

### Cómo compilar y ejecutar el Sandbox CLI

1. **Abre una terminal en la raíz del proyecto**.
2. **Ejecuta el proyecto directamente** usando `dotnet run`:
   ```bash
   dotnet run --project src/AzureDevOps.SandboxCLI/AzureDevOps.SandboxCLI.csproj
   ```

### Cómo usarlo correctamente

- **Objetivo**: La herramienta intercepta todo el tráfico HTTP entre el cliente y el servidor, mostrándolo en consola con colores estructurados.
- **Configuración de Credenciales**: Las credenciales, el endpoint (ej. `http://edvwp-tfs19-ap/`) y el dominio están configurados (hardcodeados) directamente en el archivo `src/AzureDevOps.SandboxCLI/Program.cs` por diseño, para garantizar un ambiente estricto y aislado. 
- **Verificación**: Si necesitas probar credenciales diferentes o cambiar el endpoint, simplemente edita el bloque `services.Configure<AzureDevOpsOptions>` dentro del `Program.cs` del SandboxCLI y vuelve a ejecutar la herramienta.
- **Salida en Consola**:
  - **Cyan**: Peticiones HTTP salientes (Request). Presta atención a las cabeceras `Authorization`.
  - **Amarillo**: Respuestas HTTP entrantes (Response). Presta atención al `StatusCode` y las cabeceras `WWW-Authenticate` (claves para el handshake de NTLM/Negociate).
  - **Rojo**: Errores y Excepciones puras (ej. error 401 final si falló la negociación).

Al terminar su ciclo, el proceso quedará en espera (`Console.ReadLine()`) para que puedas analizar cómodamente la salida HTTP en tu terminal.
