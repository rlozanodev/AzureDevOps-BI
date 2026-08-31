# Servicios de Fondo y Worker

El "corazón" operativo de AzureDevOps-BI es la capa de Background Services alojada en el Worker (`src/AzureDevOps.IngestionWorker`), la cual corre en paralelo a la interfaz visual (cuando se lanza desde el Desktop Manager) o de manera Headless en consola.

## 1. El Orquestador Principal (`IngestionOrchestratorJob`)
Esta clase hereda de `BackgroundService` y su método `ExecuteAsync` ejecuta un bucle infinito que despierta cada *N* segundos (según la variable `PollIntervalSeconds`).

El ciclo consta de 3 fases:
- **FASE 1: Descubrimiento de Proyectos**: Realiza llamadas a `/_apis/projects` en TFS para registrar nuevos proyectos creados en el repositorio y anotarlos en `staging.catalog_projects`.
- **FASE 2: Ingesta Aislada (Delta Sync)**: Itera proyecto por proyecto evaluando los registros en la tabla `sync_watermarks` e invoca los queries asíncronos para capturar y guardar todo en el Staging usando transacciones con `UpsertRawWorkItemsBatchAsync`. Si un proyecto devuelve un HTTP `403 Forbidden` porque el usuario no tiene permisos, marca al proyecto y continúa con los demás (tolerancia a fallos por aislamiento).
- **FASE 3: Transformación Unificada**: Una vez que se insertó toda la data cruda, lanza un proceso subyacente que levanta DuckDB (`transform_analytics.py`) para vaciar el Delta en el Data Warehouse. Adicionalmente, invoca a MSAL para refrescar los reportes de Power BI Service.

## 2. Resiliencia HTTP con Polly
El sistema incluye políticas de reintentos exponenciales para fallos transitorios. Utiliza `HttpPolicyExtensions.HandleTransientHttpError()` que cubre cortes de red y HTTP 500s (como 502/503 desde TFS), pero además captura explícitamente el código `429 Too Many Requests`. 

Para evitar avalanchas (Thundering Herd) sobre el servidor cuando se reinicia la red, Polly implementa **Retroceso Exponencial con Jitter (Ruido Aleatorio)**: espera `Math.Pow(baseDelaySeconds, retryAttempt)` sumándole una fracción aleatoria de milisegundos (`TimeSpan.FromMilliseconds(jitterer.Next(0, 500))`) entre cada reintento, logrando un comportamiento de red sumamente estable.

## 3. Tolerancia a Modificaciones Externas
- **Si el TFS se cae**: El orquestador entra en pausa, no avanza las marcas de agua, y al próximo ciclo reintenta sin generar huecos de datos.
- **Cancelación Inmediata**: Al cerrar la aplicación, el `CancellationToken` finaliza elegantemente las operaciones HTTP y de Postgres.
- **Forzado Manual**: El método `ForceSync()` desde la UI cancela el temporizador de descanso y empuja un barrido instantáneo.
