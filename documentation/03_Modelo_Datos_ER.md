# Modelo de Datos y Entidad Relación (ER)

La base de datos (PostgreSQL 16) está dividida en dos esquemas principales para garantizar el aislamiento entre la ingesta en crudo y la presentación estructurada.

## 1. Esquema `staging`
Utilizado para la persistencia rápida e idempotente de datos provenientes de TFS.

- `catalog_collections`: Lista de colecciones descubiertas.
- `catalog_projects`: Lista de proyectos descubiertos, si están habilitados (toggles UI) y si el portador tiene permisos.
- `system_configuration`: Configuraciones del sistema en formato JSON (URL, Credenciales de Power BI, opciones de UI).
- `sync_watermarks`: Estado de sincronización por entidad/proyecto. Además de la última fecha de modificación procesada (`last_watermark_utc`), monitorea rigurosamente el estado del worker: registra la hora de inicio y fin (`last_sync_start_utc`, `last_sync_end_utc`), la cantidad de registros extraídos en el último lote, y su estado en vivo (`IDLE`, `RUNNING`, `SUCCESS`, `FAILED`), incluyendo cualquier mensaje de error. Asegura que la ingesta continúe desde donde se quedó en caso de fallos.
- `raw_work_items`: Almacena el payload JSON crudo en `fields_json` (JSONB) junto con campos base indexados (`id`, `project_name`, `state`, `changed_date`). Se utiliza upsert (`ON CONFLICT`) sobre la llave principal `id`.
- `system_logs`: Recibe logs provenientes de `ILogger` de .NET para ser mostrados en la vista de la UI.

## 2. Esquema `analytics` (Modelo Estrella / Star Schema)
Un Data Warehouse tradicional estructurado para lectura masiva y conexión a Power BI.

- **Tablas de Dimensión**: `dim_date`, `dim_project`, `dim_work_item_type`, `dim_state`, `dim_iteration`, `dim_area`, `dim_member`. 
- **Tabla de Hechos Principal (`fact_work_items`)**: Contiene un registro por cada ID de Work Item junto con todas sus llaves foráneas y el cálculo de métricas de flujo (Lead, Cycle, Queue, WIP Age) pre-procesadas en columnas continuas numéricas.
- **Tabla de Hechos Snapshot (`fact_daily_flow_snapshot`)**: Almacena instantáneas (snapshots) diarias de trabajos activos (WIP), creados y rendimiento general (Throughput). Es ideal para graficar CFD (Cumulative Flow Diagrams).
- **Vistas**: `vw_throughput_weekly` y `vw_flow_metrics_summary` son vistas simplificadas optimizadas para conectar directamente a dashboards de Kanban.

## 3. Diagrama Entidad-Relación (Modelo Estrella)

```mermaid
erDiagram
    %% Dimensiones
    DIM_DATE {
        int date_key PK "YYYYMMDD"
        date full_date
        int year
        string month_name
        boolean is_weekend
    }
    DIM_PROJECT {
        int project_key PK
        string project_name
    }
    DIM_WORK_ITEM_TYPE {
        int type_key PK
        string work_item_type
        string category
    }
    DIM_STATE {
        int state_key PK
        string state_name
        string state_category
    }
    DIM_MEMBER {
        int member_key PK
        string unique_name
        string display_name
    }
    DIM_ITERATION {
        int iteration_key PK
        string iteration_path
        string sprint_name
    }
    DIM_AREA {
        int area_key PK
        string area_path
    }

    %% Hechos
    FACT_WORK_ITEMS {
        int work_item_id PK
        int project_key FK
        int type_key FK
        int state_key FK
        int iteration_key FK
        int area_key FK
        int assigned_to_key FK
        int created_by_key FK
        int created_date_key FK
        int activated_date_key FK
        int closed_date_key FK
        numeric lead_time_days
        numeric cycle_time_days
        numeric queue_time_days
        numeric wip_age_days
        numeric story_points
        boolean is_closed
        boolean is_active
    }

    FACT_DAILY_FLOW_SNAPSHOT {
        int snapshot_date_key PK, FK
        int project_key PK, FK
        int type_key PK, FK
        int state_key PK, FK
        int active_wip_count
        int completed_throughput_count
        numeric avg_cycle_time_days
    }

    %% Relaciones Fact Work Items
    FACT_WORK_ITEMS }|--|| DIM_PROJECT : "pertenece a"
    FACT_WORK_ITEMS }|--|| DIM_WORK_ITEM_TYPE : "es de tipo"
    FACT_WORK_ITEMS }|--|| DIM_STATE : "esta en estado"
    FACT_WORK_ITEMS }|--|| DIM_MEMBER : "asignado a"
    FACT_WORK_ITEMS }|--|| DIM_MEMBER : "creado por"
    FACT_WORK_ITEMS }|--|| DIM_ITERATION : "asignado a sprint"
    FACT_WORK_ITEMS }|--|| DIM_AREA : "asignado a area"
    FACT_WORK_ITEMS }|--|| DIM_DATE : "creado en"
    FACT_WORK_ITEMS }|--|| DIM_DATE : "activado en"
    FACT_WORK_ITEMS }|--|| DIM_DATE : "cerrado en"

    %% Relaciones Fact Snapshot
    FACT_DAILY_FLOW_SNAPSHOT }|--|| DIM_DATE : "fotografía del dia"
    FACT_DAILY_FLOW_SNAPSHOT }|--|| DIM_PROJECT : "proyecto"
    FACT_DAILY_FLOW_SNAPSHOT }|--|| DIM_WORK_ITEM_TYPE : "tipo"
    FACT_DAILY_FLOW_SNAPSHOT }|--|| DIM_STATE : "estado"
```
