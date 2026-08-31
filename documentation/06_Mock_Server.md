# Mock Server (Simulador de TFS)

Dado que es un requisito de negocio extraer datos desde instancias *On-Premise* restringidas (TFS 2019), durante el ciclo de desarrollo local (laptop del desarrollador) era inviable tener levantado un Azure DevOps Server (IIS) completo debido al excesivo costo de hardware y licencias que esto conlleva.

Para sortear esto, se desarrolló `src/AzureDevOps.MockServer`.

## 1. ¿Qué es el Mock Server?
Es un servidor web ligero construido con Minimal APIs en .NET. Mimetiza la firma, rutas, expansión ( `$expand` ) y los contratos JSON de las dos APIs fundamentales requeridas de la versión `5.0-preview` de TFS:
- El EndPoint de peticiones WIQL (`/_apis/wit/wiql`).
- El EndPoint de extracciones masivas (`/_apis/wit/workitems`).

## 2. Generación Dinámica de Datos
En el arranque (`Program.cs`), el Mock Server **semilla en memoria** 250 Work Items con variaciones aleatorias pero lógicas simulando una colección de 4 proyectos (`CoreBanking`, `MobileApp`, `PaymentGateway`, `DevOpsPlatform`).

Garantiza la lógica de las fechas y métricas:
- Simula distintos `WorkItemType` (Epics, Bugs, User Stories).
- Simula asignaciones y creadores con correos electrónicos ficticios.
- Siembra lógicamente el *Timeline* (Ej: `ClosedDate` siempre es posterior a `ActivatedDate` y este posterior a `CreatedDate`).
- Muta los Story Points, Esfuerzo original y trabajo restante en función de la madurez del ticket, emulando la realidad.

## 3. Comportamientos Simulados para Testing E2E
El simulador valida y rechaza operaciones de la misma manera que el TFS productivo:
- **Límites de Batching**: Si se le piden más de 200 ítems en el string del query de `/_apis/wit/workitems?ids=...`, retorna automáticamente un `HTTP 400 BadRequest`.
- **Soporte WIQL Delta**: Al recibir peticiones POST a WIQL, la Minimal API parsea mediante Regex el query, buscando `[System.ChangedDate] > 'YYYY-MM-DD'` y filtra la tabla en memoria devolviendo solo las referencias a ítems nuevos.

## 4. Uso Principal
Permite que todos los test de integración y pruebas E2E (End to End) corran en cualquier máquina Linux/Windows o Pipeline de CI/CD (GitHub Actions) apuntando a `http://localhost:5000` sin necesidad de conectividad VPN ni licencias TFS.
