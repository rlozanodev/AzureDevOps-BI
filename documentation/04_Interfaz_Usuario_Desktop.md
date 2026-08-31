# Interfaz Gráfica (Desktop Manager)

AzureDevOps-BI no es una aplicación web, sino una aplicación de **escritorio nativa** construida con **Avalonia UI** (C# .NET 9). Esta decisión se tomó para garantizar portabilidad (Zero-Install), bajo consumo de memoria y evadir restricciones de red corporativas.

## 1. Arquitectura de UI y Patrones
Se implementa el patrón **MVVM (Model-View-ViewModel)** sin enrutamiento estricto de comandos (`CompiledBindings="False"`). En su lugar, se utilizan manejadores de eventos en el Code-Behind (ej: `Click="OnButtonClick"`) que luego delegan la ejecución asíncrona al ViewModel para no bloquear el hilo de la interfaz (UI Thread).

La aplicación y el motor de sincronización (background worker) **comparten un único contenedor `IHost` (`App.AppHost`)**. Esto significa que las configuraciones, bases de datos y orquestación operan en el mismo ciclo de vida que la ventana principal de la interfaz, posibilitando actualización en tiempo real de logs y métricas.

## 2. Pestañas y Componentes

La ventana principal (`MainWindow.axaml`) funciona como *Layout Base* ofreciendo una barra lateral y una cabecera con un *Flyout* para perfiles (muestra la cuenta actual de Windows de quien ejecuta la app) y un botón de modo Claro/Oscuro dinámico. Este botón usa `RequestedThemeVariant` y `DynamicResource` en Avalonia para alterar la paleta de colores de toda la interfaz en tiempo real sin recargar la aplicación.

El contenido cambia a través de Vistas de Usuario (`UserControls`):
1. **Dashboard (`DashboardTab.axaml`)**: Muestra métricas de alto nivel (Work items extraídos, Proyectos analizados) y el estado de sincronización global de la aplicación (Idle, Running, Offline, Error).
2. **Catálogo (`MappingTab.axaml`)**: Interfaz de autodescubrimiento. Lista todas las colecciones y proyectos de TFS que se encontraron mediante la API. Permite habilitar/deshabilitar (`Toggles`) la extracción proyecto por proyecto.
3. **Configuración (`ConfigTab.axaml`)**: Permite ajustar la URL Base del TFS, cambiar de Autenticación Integrada de Windows a Autenticación Explícita, y configurar el puente automatizado hacia Power BI Web (Tenant, Client ID, Secret).
4. **Logs (`LogsTab.axaml`)**: Visor de logs dinámico y paginado (por fecha). En lugar de escribir en consola, todo lo que produce `ILogger` se captura mediante un proveedor custom (`DatabaseLoggerProvider`) y se inserta en PostgreSQL (`staging.system_logs`). En el momento del arranque, una tarea asíncrona (`InitializeSchemaAsync`) asegura de forma resiliente que la tabla de logs exista. Esta pestaña lee dichos logs, permitiendo búsqueda y copia.

## 3. Minimización en la Bandeja del Sistema (System Tray)
La aplicación nunca detiene la recolección a menos que el usuario la cierre forzosamente. Cuando se da clic en la "X" (Cerrar), la aplicación **se esconde en la bandeja del sistema (junto al reloj)**, dejando un ícono de notificación visible y manteniendo el orquestador (`IngestionOrchestratorJob`) corriendo en segundo plano sin consumir recursos gráficos.
