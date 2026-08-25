# Guía de Arquitectura, Flujo de Trabajo y Puesta en Marcha
## Sistema de Ingesta (Headless & Desktop UI), Procesamiento y Automatización de Métricas de Azure DevOps Server (TFS 2019) para Power BI Web

---

## 1. Resumen Ejecutivo: ¿Qué se hizo?

Se ha diseñado e implementado desde el cero absoluto una **plataforma desacoplada, modular, resiliente e idempotente** para extraer datos de Work Items desde una instancia local **On-Premise de Azure DevOps Server (TFS 2019)**, almacenarlos en una base de datos **PostgreSQL 16**, transformarlos vectorialmente con un motor OLAP de ultra alto rendimiento (**Python + DuckDB + uv**) hacia un **modelo dimensional en estrella (Star Schema)**, y automatizar el refresco del dataset en **Power BI Web (Service Principal / REST API)** sin intervención humana.

Además, se ha incorporado una **interfaz gráfica de escritorio (Desktop Manager en Avalonia UI)** que permite monitorear y gestionar visualmente la ingesta, ver métricas en tiempo real, configurar credenciales NTLM, y minimizar la aplicación a la bandeja del sistema (System Tray).

```text
+---------------------------------------------------------------------------------------------------+
|                                 ARQUITECTURA GENERAL DEL SISTEMA                                  |
+---------------------------------------------------------------------------------------------------+
|                                                                                                   |
|   [ Azure DevOps Server 2019 ]                                                                    |
|   (On-Premise / IIS / NTLM Auth)                                                                  |
|               │                                                                                   |
|               │  1. WIQL (Delta Sync) + Batch 200 items (api-version=5.0-preview)                 |
|               ▼                                                                                   |
|   [ .NET 8/9 Worker Service / Avalonia Desktop UI ] ──(Polly Retries + Windows NTLM Auth)         |
|               │                                                                                   |
|               │  2. Upsert Idempotente (Dapper)                                                   |
|               ▼                                                                                   |
|   [ PostgreSQL 16 (Docker) ] ── schema: staging (raw_work_items + sync_watermarks)                |
|               │                                                                                   |
|               │  3. ATTACH Nativo DuckDB & Cálculo Vectorial de Métricas                          |
|               ▼                                                                                   |
|   [ Python + DuckDB + uv ] ─── schema: analytics (dim_*, fact_work_items, fact_daily_flow, views) |
|               │                                                                                   |
|               │  4. MSAL OAuth2 Service Principal Refresh                                         |
|               ▼                                                                                   |
|   [ Power BI Web REST API ] ─── Refresh Dataset Automático en Power BI Service                    |
|                                                                                                   |
+---------------------------------------------------------------------------------------------------+
```

---

## 2. Decisiones de Arquitectura: ¿Por qué se hizo así?

### A. Restricciones del Servidor TFS 2019 On-Premise
1. **Autenticación Integrada de Windows (NTLM / Kerberos / SSO)**:
   - *Motivo*: Tu IIS local no soporta Personal Access Tokens (PATs) por HTTP.
   - *Solución*: En .NET, `NtlmHttpHandlerFactory` configura el `HttpMessageHandler` con `UseDefaultCredentials = true` y `CredentialCache.DefaultNetworkCredentials` (o credenciales explícitas de dominio/usuario/contraseña si se ejecuta desde un entorno Linux o contenedor).
2. **Versión de API Obligatoria (`api-version=5.0-preview`)**:
   - *Motivo*: Compatibilidad nativa estricta con TFS 2019 RTW / Update 1.
   - *Solución*: Todas las peticiones HTTP construidas por `AzureDevOpsClient` inyectan automáticamente el query parameter `api-version=5.0-preview`.
3. **Mecánica de Extracción WIQL + Batching de 200 ítems**:
   - *Motivo*: La API de Work Items no permite descargar masivamente sin límite en una sola llamada y saturaría el IIS.
   - *Solución*: Primero se ejecuta una consulta WIQL ligera (`wit/wiql`) filtrando `[System.ChangedDate] > '{watermark}'` para obtener exclusivamente los IDs modificados. Luego, mediante `StreamWorkItemBatchesAsync`, se divide el listado en lotes de **máximo 200 ítems** con `$expand=all`.

### B. Persistencia y Dos Esquemas (PostgreSQL 16)
- **Esquema `staging`**:
  - `staging.sync_watermarks`: Almacena el estado de la sincronización delta, la última fecha de modificación procesada (`last_watermark_utc`), la duración y el estado (`IDLE`, `RUNNING`, `SUCCESS`, `FAILED`).
  - `staging.raw_work_items`: Almacena los registros crudos y semi-estructurados con los campos principales indexados y el payload completo en `fields_json (JSONB)`.
  - **Idempotencia Absoluta**: Se utiliza la cláusula `ON CONFLICT (id) DO UPDATE SET ...` en Dapper. Si el proceso se interrumpe a la mitad y vuelve a sincronizar, **nunca se generan duplicados**.
- **Esquema `analytics`**:
  - Modelo en estrella optimizado para Power BI (DirectQuery o Import):
    - `dim_date`, `dim_project`, `dim_work_item_type`, `dim_state`, `dim_iteration`, `dim_area`, `dim_member`.
    - `fact_work_items`: Nivel de granularidad individual de Work Item con todas las métricas de flujo calculadas.
    - `fact_daily_flow_snapshot`: Agregaciones históricas diarias de Throughput, WIP activo y tiempos promedio.
    - Vistas analíticas: `vw_flow_metrics_summary` y `vw_throughput_weekly`.

### C. Motor de Transformación (Python + DuckDB + uv)
- *Motivo*: Realizar cálculos analíticos en memoria sin mover datos por la red y sin cargar pesadas librerías de Spark o Pandas tradicionales.
- *Solución*: DuckDB se conecta directamente a PostgreSQL mediante su extensión nativa (`INSTALL postgres; LOAD postgres; ATTACH ... AS pg (TYPE POSTGRES);`). Ejecuta transformaciones vectorizadas en subsegundos:
  - **Lead Time**: `ClosedDate - CreatedDate` (en días).
  - **Cycle Time**: `ClosedDate - ActivatedDate` (en días).
  - **Queue Time (Tiempo en cola)**: `ActivatedDate - CreatedDate` (en días).
  - **WIP Age (Antigüedad del trabajo en curso)**: `NOW() - COALESCE(ActivatedDate, CreatedDate)` (para ítems activos).
  - **Throughput**: Cantidad de ítems finalizados por ventana de tiempo / sprint.

### D. Orquestación y Refresco Automático en Power BI Web
- *Motivo*: Una vez que los datos dimensionales están actualizados, el informe en Power BI Web debe reflejar la información inmediatamente sin esperar a los programadores de refresco de Power BI.
- *Solución*: El Worker invoca `PowerBiRefreshService`, el cual obtiene un token OAuth2 mediante **MSAL (Azure AD Service Principal / App Registration)** y dispara `RefreshDatasetInGroupAsync` en la Power BI REST API.

### E. Desktop Manager UI (Avalonia) y AutoDiscover
- *Motivo*: Proporcionar una forma amigable de gestionar configuraciones (incluyendo credenciales NTLM), ver los logs y el estado del worker en tiempo real, sin depender exclusivamente de consolas, en aquellos entornos donde no se requiera ejecución por consola pura.
- *Solución*: Se creó `AzureDevOps.DesktopManager` utilizando **Avalonia UI** y el patrón **MVVM**. Esta aplicación carga la configuración original del Worker (`AutoDiscover` del `appsettings.json`) y ejecuta el ciclo principal de ingestión internamente como un *Hosted Service*. Permite la configuración desde una UI dividida en pestañas (Dashboard, Config, Mapping, Logs) y la minimización a la bandeja del sistema (System Tray).

---

## 3. Estructura Completa del Proyecto

```
AzureDevOps-BI/
├── .env                              # Variables de entorno locales (credenciales, URLs)
├── .env.example                      # Plantilla de variables de entorno
├── .gitignore                        # Reglas de exclusión de Git
├── compose.yaml                      # Definición de Docker Compose (Postgres 16 + Adminer)
├── README.md                         # Resumen del repositorio
├── ARQUITECTURA_Y_GUIA_DE_USO.md     # Esta guía completa
│
├── docker/
│   └── postgres/
│       └── init.sql                  # Script DDL de esquemas staging y analytics, tablas y vistas
│
├── analytics_engine/                 # Motor de Transformación OLAP (Python + DuckDB)
│   ├── pyproject.toml                # Definición del entorno gestionado con uv
│   ├── README.md                     # Documentación interna del motor
│   └── transform_analytics.py        # Script de transformación dimensional vectorizada
│
├── src/
│   ├── AzureDevOps.Core/             # Modelos, DTOs, Entidades, Configuración e Interfaces
│   │   ├── Configuration/            # AzureDevOpsOptions, DatabaseOptions, PowerBiOptions...
│   │   ├── Models/                   # Wiql DTOs, WorkItem DTOs, RawWorkItemEntity, Watermark...
│   │   └── Interfaces/               # IAzureDevOpsClient, IWorkItemStagingRepository...
│   │
│   ├── AzureDevOps.DesktopManager/   # Interfaz Gráfica (Avalonia UI) y Host del Worker
│   │   ├── App.axaml                 # Configuración principal y System Tray
│   │   ├── Program.cs                # Entrypoint de Desktop
│   │   ├── ViewModels/               # ViewModel para la UI (MVVM)
│   │   └── Views/                    # Vistas y pestañas (Dashboard, Config, Mapping, Logs)
│   │
│   ├── AzureDevOps.IngestionWorker/  # Worker Service en .NET 8/9 (Modo Consola)
│   │   ├── appsettings.json          # Configuración base
│   │   ├── appsettings.Development.json
│   │   ├── Program.cs                # Entrypoint, DI, Serilog, Polly Retry Policies, NTLM Handler
│   │   ├── Jobs/
│   │   │   └── IngestionOrchestratorJob.cs   # BackgroundService principal (Delta Sync Loop)
│   │   └── Services/
│   │       ├── AzureDevOps/          # AzureDevOpsClient y NtlmHttpHandlerFactory
│   │       ├── Database/             # WorkItemStagingRepository con consultas Dapper
│   │       ├── Transformation/       # PythonTransformationService (ejecución de DuckDB vía uv)
│   │       └── PowerBI/              # PowerBiRefreshService (SDK Microsoft.PowerBI.Api + MSAL)
│   │
│   └── AzureDevOps.MockServer/       # Servidor simulador de TFS 2019 para pruebas locales y E2E
│       └── Program.cs
│
└── tests/
    └── AzureDevOps.Tests/            # Suite de pruebas unitarias y de integración (xUnit + FluentAssertions)
        ├── AzureDevOpsClientTests.cs
        ├── WorkItemMappingTests.cs
        ├── StagingRepositoryTests.cs
        └── EndToEndPipelineTests.cs
```

---

## 4. Archivos a Revisar y Modificar para Conectar a tu TFS 2019

Para acoplar el sistema a tu infraestructura real, únicamente debes revisar y ajustar los siguientes archivos de configuración:

### Archivo 1: `.env` (en la raíz del proyecto)

Abre o crea el archivo `.env` y configura los valores correspondientes a tu red y servidor:

```ini
# ==============================================================================
# 1. BASE DE DATOS LOCAL (DOCKER POSTGRESQL)
# ==============================================================================
POSTGRES_DB=azure_devops_dw
POSTGRES_USER=postgres
POSTGRES_PASSWORD=TuPasswordSeguro123!
POSTGRES_PORT=5432
ADMINER_PORT=8080

# Cadena de conexión para .NET y Python
DB_HOST=localhost
DB_PORT=5432
ConnectionStrings__PostgresDb="Host=localhost;Port=5432;Database=azure_devops_dw;Username=postgres;Password=TuPasswordSeguro123!;Include Error Detail=true;"

# ==============================================================================
# 2. AZURE DEVOPS SERVER 2019 (ON-PREMISE)
# ==============================================================================
# URL base de tu servidor TFS local
AzureDevOps__BaseUrl=http://edvwp-tfs19-ap/

# Colección (por defecto DefaultCollection o el nombre de tu colección)
AzureDevOps__Collection=DefaultCollection

# Proyecto específico (dejar vacío si deseas sincronizar TODA la colección)
AzureDevOps__Project=

# Versión de API requerida para TFS 2019
AzureDevOps__ApiVersion=5.0-preview

# Tamaño de lote (máximo 200 por restricciones de TFS)
AzureDevOps__BatchSize=200

# Intervalo entre sincronizaciones en segundos (ej. 300 = cada 5 minutos)
AzureDevOps__PollIntervalSeconds=300

# ------------------------------------------------------------------------------
# AUTENTICACIÓN INTEGRADA DE WINDOWS (NTLM / SSPI)
# ------------------------------------------------------------------------------
# CASO A: Si el Worker corre en una máquina Windows unida al Dominio o bajo una Service Account de Windows:
AzureDevOps__Auth__UseDefaultCredentials=true
AzureDevOps__Auth__Domain=
AzureDevOps__Auth__Username=
AzureDevOps__Auth__Password=

# CASO B: Si el Worker corre desde Linux / Docker y necesita autenticarse contra el TFS con usuario de dominio:
# AzureDevOps__Auth__UseDefaultCredentials=false
# AzureDevOps__Auth__Domain=MIDOMINIO
# AzureDevOps__Auth__Username=mi_usuario_servicio
# AzureDevOps__Auth__Password=mi_password_servicio

# ==============================================================================
# 3. MOTOR DE TRANSFORMACIÓN (PYTHON & DUCKDB)
# ==============================================================================
Transformation__Enabled=true
Transformation__PythonExecutable=uv
Transformation__ScriptPath=./analytics_engine/transform_analytics.py
Transformation__TimeoutSeconds=300

# ==============================================================================
# 4. POWER BI WEB REST API (SERVICE PRINCIPAL)
# ==============================================================================
# Si deseas que se refresque automáticamente Power BI Web, ponlo en true y completa los GUIDs
PowerBi__Enabled=false
PowerBi__TenantId=00000000-0000-0000-0000-000000000000
PowerBi__ClientId=00000000-0000-0000-0000-000000000000
PowerBi__ClientSecret=TU_CLIENT_SECRET_DE_AZURE_AD
PowerBi__WorkspaceId=00000000-0000-0000-0000-000000000000
PowerBi__DatasetId=00000000-0000-0000-0000-000000000000
```

### Archivo 2: `src/AzureDevOps.IngestionWorker/appsettings.json`

Este archivo contiene la configuración de respaldo en caso de no utilizar variables de entorno. Puedes mantenerlo sincronizado con `.env` o definir perfiles específicos en `appsettings.Development.json`.
> **Importante (AutoDiscover)**: La interfaz gráfica de `AzureDevOps.DesktopManager` buscará automáticamente este archivo `appsettings.json` en el directorio de `IngestionWorker` y cargará sus valores, por lo que es la fuente central de verdad si ejecutas desde la UI.

---

## 5. Guía de Ejecución y Puesta en Marcha Paso a Paso

Sigue estos pasos en tu terminal para iniciar y operar el sistema:

### Paso 1: Levantar la Base de Datos (PostgreSQL + Adminer)
Ejecuta el contenedor de Docker:
```bash
docker compose up -d
```
Verifica que los contenedores estén saludables:
```bash
docker compose ps
```
> **Nota**: Puedes abrir tu navegador en `http://localhost:8080` para acceder a Adminer (Sistema: `PostgreSQL`, Servidor: `postgres`, Usuario: `postgres`, Contraseña: la configurada en `.env`, BD: `azure_devops_dw`).

### Paso 2: Preparar el Entorno de Python con `uv`
En la raíz del proyecto, sincroniza las dependencias del motor OLAP:
```bash
cd analytics_engine
uv sync
cd ..
```
Puedes probar la transformación manual en cualquier momento con:
```bash
uv run analytics_engine/transform_analytics.py
```

### Paso 3: Ejecutar la Suite de Pruebas Unitarias e Integración
Verifica que todo el sistema compile y pase las pruebas:
```bash
dotnet test
```

### Paso 4: Iniciar el Sistema (Worker CLI o Desktop UI)

**Opción A: Ejecutar el Worker Headless (Consola / Servidor)**
```bash
dotnet run --project src/AzureDevOps.IngestionWorker/AzureDevOps.IngestionWorker.csproj
```

**Opción B: Ejecutar la Interfaz Gráfica (Desktop Manager)**
```bash
dotnet run --project src/AzureDevOps.DesktopManager/AzureDevOps.DesktopManager.csproj
```
*(Al iniciar con el Desktop Manager, dispondrás de un Dashboard visual, logs en vivo, y podrás minimizar la aplicación a la bandeja del sistema)*
El Worker realizará inmediatamente:
1. Lectura de la marca de agua (`staging.sync_watermarks`).
2. Consulta WIQL incremental contra tu servidor `http://edvwp-tfs19-ap/`.
3. Descarga en lotes de 200 ítems con reintentos Polly.
4. Upsert idempotente en `staging.raw_work_items`.
5. Ejecución del motor DuckDB para poblar el Star Schema en `analytics`.
6. Disparo del refresco del dataset en Power BI Web (si está habilitado).
7. Espera en reposo hasta el próximo ciclo (`PollIntervalSeconds`).

---

## 6. Conexión de Power BI Desktop / Web al Modelo Dimensional

Para construir tus tableros en Power BI Desktop:

1. Abre **Power BI Desktop** -> **Obtener datos** -> **Base de datos PostgreSQL**.
2. **Servidor**: `localhost:5432` (o la IP del servidor donde esté alojado Postgres).
3. **Base de datos**: `azure_devops_dw`.
4. **Modo de Conectividad**: Selecciona **Import** o **DirectQuery**.
5. Selecciona las siguientes tablas y vistas del esquema **`analytics`**:
   - `analytics.dim_date`
   - `analytics.dim_project`
   - `analytics.dim_work_item_type`
   - `analytics.dim_state`
   - `analytics.dim_iteration`
   - `analytics.dim_area`
   - `analytics.dim_member`
   - `analytics.fact_work_items`
   - `analytics.fact_daily_flow_snapshot`
   - `analytics.vw_flow_metrics_summary`
   - `analytics.vw_throughput_weekly`

### Relaciones del Modelo Dimensional en Power BI:
- `fact_work_items[created_date_key]` ───> `dim_date[date_key]` (Activa)
- `fact_work_items[closed_date_key]` ────> `dim_date[date_key]` (Inactiva o relación para Throughput)
- `fact_work_items[project_key]` ────────> `dim_project[project_key]`
- `fact_work_items[type_key]` ───────────> `dim_work_item_type[type_key]`
- `fact_work_items[state_key]` ──────────> `dim_state[state_key]`
- `fact_work_items[iteration_key]` ──────> `dim_iteration[iteration_key]`
- `fact_work_items[area_key]` ───────────> `dim_area[area_key]`
- `fact_work_items[assigned_to_key]` ────> `dim_member[member_key]`

---

## 7. Preguntas Frecuentes y Solución de Problemas

### ¿Qué pasa si el servidor TFS pierde conexión durante la sincronización?
Polly reintentará automáticamente la petición HTTP con retroceso exponencial. Si el fallo persiste, el Worker registrará el error en `staging.sync_watermarks` con estado `FAILED` y la marca de agua no avanzará. En el siguiente ciclo, reanudará la sincronización desde el último punto seguro sin duplicar registros gracias a `ON CONFLICT (id) DO UPDATE`.

### ¿Cómo forzar una resincronización completa desde cero?
Si deseas reprocesar todos los Work Items históricos de tu colección, ejecuta en PostgreSQL:
```sql
UPDATE staging.sync_watermarks
SET last_watermark_utc = '1970-01-01 00:00:00+00'
WHERE entity_name = 'work_items';
```
En el siguiente ciclo del Worker, la consulta WIQL traerá todos los ítems de la historia y actualizará `staging` y `analytics`.
