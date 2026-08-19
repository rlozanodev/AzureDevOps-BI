-- =============================================================================
-- AZURE DEVOPS BI - INITIAL DATABASE SCHEMA
-- PostgreSQL 16
-- Schemas: staging (raw / semi-structured), analytics (star schema)
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- -----------------------------------------------------------------------------
-- SCHEMAS CREATION
-- -----------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS staging;
CREATE SCHEMA IF NOT EXISTS analytics;

-- -----------------------------------------------------------------------------
-- 1. STAGING SCHEMA: WATERMARKING & RAW DATA
-- -----------------------------------------------------------------------------

-- Control de marcas de agua para sincronización incremental (Delta Sync)
CREATE TABLE IF NOT EXISTS staging.sync_watermarks (
    entity_name VARCHAR(100) NOT NULL,
    collection_name VARCHAR(255) NOT NULL DEFAULT 'DefaultCollection',
    project_name VARCHAR(255) NOT NULL DEFAULT '',
    last_watermark_utc TIMESTAMPTZ NOT NULL DEFAULT '1970-01-01 00:00:00+00',
    last_sync_start_utc TIMESTAMPTZ,
    last_sync_end_utc TIMESTAMPTZ,
    status VARCHAR(50) NOT NULL DEFAULT 'IDLE',
    records_extracted_last_run INT DEFAULT 0,
    error_message TEXT,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sync_watermarks PRIMARY KEY (entity_name, collection_name, project_name)
);

-- Tabla staging de Work Items (Upserts idempotentes vía Dapper / NpgsqlBatch)
CREATE TABLE IF NOT EXISTS staging.raw_work_items (
    id INT PRIMARY KEY,
    rev INT NOT NULL,
    url TEXT,
    project_name VARCHAR(255) NOT NULL,
    work_item_type VARCHAR(100) NOT NULL,
    title TEXT,
    state VARCHAR(100) NOT NULL,
    reason VARCHAR(255),
    assigned_to_name VARCHAR(255),
    assigned_to_unique_name VARCHAR(255),
    created_by_name VARCHAR(255),
    created_by_unique_name VARCHAR(255),
    created_date TIMESTAMPTZ,
    changed_date TIMESTAMPTZ NOT NULL,
    activated_date TIMESTAMPTZ,
    closed_date TIMESTAMPTZ,
    state_change_date TIMESTAMPTZ,
    story_points NUMERIC(10,2),
    original_estimate NUMERIC(10,2),
    remaining_work NUMERIC(10,2),
    completed_work NUMERIC(10,2),
    priority INT,
    severity VARCHAR(100),
    area_path TEXT,
    iteration_path TEXT,
    tags TEXT,
    fields_json JSONB NOT NULL,
    ingested_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_raw_work_items_changed_date ON staging.raw_work_items (changed_date DESC);
CREATE INDEX IF NOT EXISTS idx_raw_work_items_project ON staging.raw_work_items (project_name);
CREATE INDEX IF NOT EXISTS idx_raw_work_items_state ON staging.raw_work_items (state);
CREATE INDEX IF NOT EXISTS idx_raw_work_items_type ON staging.raw_work_items (work_item_type);
CREATE INDEX IF NOT EXISTS idx_raw_work_items_iteration ON staging.raw_work_items (iteration_path);

-- -----------------------------------------------------------------------------
-- 2. ANALYTICS SCHEMA: STAR SCHEMA (DIMENSIONS & FACTS)
-- -----------------------------------------------------------------------------

-- Dimensión Tiempo / Calendario
CREATE TABLE IF NOT EXISTS analytics.dim_date (
    date_key INT PRIMARY KEY, -- Formato YYYYMMDD
    full_date DATE NOT NULL UNIQUE,
    year INT NOT NULL,
    quarter INT NOT NULL,
    quarter_name VARCHAR(10) NOT NULL,
    month INT NOT NULL,
    month_name VARCHAR(20) NOT NULL,
    month_short_name VARCHAR(10) NOT NULL,
    week_of_year INT NOT NULL,
    day_of_month INT NOT NULL,
    day_of_week INT NOT NULL,
    day_name VARCHAR(20) NOT NULL,
    is_weekend BOOLEAN NOT NULL,
    is_business_day BOOLEAN NOT NULL,
    year_month INT NOT NULL -- YYYYMM
);

-- Poblado de DimDate (2018 - 2035)
INSERT INTO analytics.dim_date
SELECT
    TO_CHAR(datum, 'YYYYMMDD')::INT AS date_key,
    datum AS full_date,
    EXTRACT(YEAR FROM datum)::INT AS year,
    EXTRACT(QUARTER FROM datum)::INT AS quarter,
    'Q' || EXTRACT(QUARTER FROM datum)::TEXT AS quarter_name,
    EXTRACT(MONTH FROM datum)::INT AS month,
    TO_CHAR(datum, 'TMMonth') AS month_name,
    TO_CHAR(datum, 'TMMon') AS month_short_name,
    EXTRACT(WEEK FROM datum)::INT AS week_of_year,
    EXTRACT(DAY FROM datum)::INT AS day_of_month,
    EXTRACT(ISODOW FROM datum)::INT AS day_of_week,
    TO_CHAR(datum, 'TMDay') AS day_name,
    CASE WHEN EXTRACT(ISODOW FROM datum) IN (6, 7) THEN TRUE ELSE FALSE END AS is_weekend,
    CASE WHEN EXTRACT(ISODOW FROM datum) IN (1, 2, 3, 4, 5) THEN TRUE ELSE FALSE END AS is_business_day,
    TO_CHAR(datum, 'YYYYMM')::INT AS year_month
FROM generate_series('2018-01-01'::DATE, '2035-12-31'::DATE, '1 day'::INTERVAL) datum
ON CONFLICT (date_key) DO NOTHING;

-- Registro para fechas nulas / desconocidas
INSERT INTO analytics.dim_date (
    date_key, full_date, year, quarter, quarter_name, month, month_name,
    month_short_name, week_of_year, day_of_month, day_of_week, day_name, is_weekend, is_business_day, year_month
) VALUES (
    -1, '1900-01-01', 1900, 1, 'N/A', 1, 'Unknown', 'Unk', 1, 1, 1, 'Unknown', FALSE, FALSE, 190001
) ON CONFLICT (date_key) DO NOTHING;

-- Dimensión Proyecto
CREATE TABLE IF NOT EXISTS analytics.dim_project (
    project_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_name VARCHAR(255) NOT NULL UNIQUE,
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Dimensión Tipo de Work Item
CREATE TABLE IF NOT EXISTS analytics.dim_work_item_type (
    type_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    work_item_type VARCHAR(100) NOT NULL UNIQUE,
    category VARCHAR(100) NOT NULL DEFAULT 'Requirement', -- Epic, Feature, Requirement, Bug, Task
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Dimensión Estado (State & Category Mapping)
CREATE TABLE IF NOT EXISTS analytics.dim_state (
    state_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    state_name VARCHAR(100) NOT NULL UNIQUE,
    state_category VARCHAR(100) NOT NULL DEFAULT 'Proposed', -- Proposed, In Progress, Resolved, Completed, Removed
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Dimensión Iteración / Sprint
CREATE TABLE IF NOT EXISTS analytics.dim_iteration (
    iteration_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    iteration_path TEXT NOT NULL UNIQUE,
    sprint_name VARCHAR(255),
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Dimensión Área
CREATE TABLE IF NOT EXISTS analytics.dim_area (
    area_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    area_path TEXT NOT NULL UNIQUE,
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Dimensión Miembro del Equipo / Usuario
CREATE TABLE IF NOT EXISTS analytics.dim_member (
    member_key INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    unique_name VARCHAR(255) NOT NULL UNIQUE,
    display_name VARCHAR(255) NOT NULL,
    created_at_utc TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Registros por defecto "Unknown / Unassigned" para cada dimensión (clave = 0 o ID 1)
-- En PostgreSQL con IDENTITY, se insertan registros estándar
INSERT INTO analytics.dim_project (project_name) VALUES ('Desconocido') ON CONFLICT (project_name) DO NOTHING;
INSERT INTO analytics.dim_work_item_type (work_item_type, category) VALUES ('Desconocido', 'Unknown') ON CONFLICT (work_item_type) DO NOTHING;
INSERT INTO analytics.dim_state (state_name, state_category) VALUES ('Desconocido', 'Unknown') ON CONFLICT (state_name) DO NOTHING;
INSERT INTO analytics.dim_iteration (iteration_path, sprint_name) VALUES ('Desconocido', 'Desconocido') ON CONFLICT (iteration_path) DO NOTHING;
INSERT INTO analytics.dim_area (area_path) VALUES ('Desconocido') ON CONFLICT (area_path) DO NOTHING;
INSERT INTO analytics.dim_member (unique_name, display_name) VALUES ('unassigned', 'Sin Asignar') ON CONFLICT (unique_name) DO NOTHING;

-- Tabla de Hechos: Work Items & Métricas de Flujo (Lead Time, Cycle Time, Queue Time, WIP Age)
CREATE TABLE IF NOT EXISTS analytics.fact_work_items (
    work_item_id INT PRIMARY KEY,
    rev INT NOT NULL,
    title TEXT,
    project_key INT NOT NULL REFERENCES analytics.dim_project(project_key),
    type_key INT NOT NULL REFERENCES analytics.dim_work_item_type(type_key),
    state_key INT NOT NULL REFERENCES analytics.dim_state(state_key),
    iteration_key INT NOT NULL REFERENCES analytics.dim_iteration(iteration_key),
    area_key INT NOT NULL REFERENCES analytics.dim_area(area_key),
    assigned_to_key INT NOT NULL REFERENCES analytics.dim_member(member_key),
    created_by_key INT NOT NULL REFERENCES analytics.dim_member(member_key),
    created_date_key INT NOT NULL REFERENCES analytics.dim_date(date_key),
    activated_date_key INT NOT NULL REFERENCES analytics.dim_date(date_key),
    closed_date_key INT NOT NULL REFERENCES analytics.dim_date(date_key),
    changed_date_key INT NOT NULL REFERENCES analytics.dim_date(date_key),
    created_date_utc TIMESTAMPTZ,
    activated_date_utc TIMESTAMPTZ,
    closed_date_utc TIMESTAMPTZ,
    changed_date_utc TIMESTAMPTZ,
    lead_time_days NUMERIC(10,2),   -- Tiempo total: CreatedDate -> ClosedDate
    cycle_time_days NUMERIC(10,2),  -- Tiempo activo: ActivatedDate -> ClosedDate
    queue_time_days NUMERIC(10,2),  -- Tiempo en espera: CreatedDate -> ActivatedDate
    wip_age_days NUMERIC(10,2),     -- Antigüedad si está activo: ActivatedDate -> NOW (o CreatedDate -> NOW si no activado)
    is_closed BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    story_points NUMERIC(10,2),
    original_estimate NUMERIC(10,2),
    remaining_work NUMERIC(10,2),
    completed_work NUMERIC(10,2),
    priority INT,
    severity VARCHAR(100),
    tags TEXT,
    last_transformed_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_fact_work_items_project ON analytics.fact_work_items(project_key);
CREATE INDEX IF NOT EXISTS idx_fact_work_items_type ON analytics.fact_work_items(type_key);
CREATE INDEX IF NOT EXISTS idx_fact_work_items_state ON analytics.fact_work_items(state_key);
CREATE INDEX IF NOT EXISTS idx_fact_work_items_closed_date ON analytics.fact_work_items(closed_date_key);
CREATE INDEX IF NOT EXISTS idx_fact_work_items_iteration ON analytics.fact_work_items(iteration_key);

-- Tabla de Hechos: Instantáneas Diarias y Throughput Agregado
CREATE TABLE IF NOT EXISTS analytics.fact_daily_flow_snapshot (
    snapshot_date_key INT NOT NULL REFERENCES analytics.dim_date(date_key),
    project_key INT NOT NULL REFERENCES analytics.dim_project(project_key),
    type_key INT NOT NULL REFERENCES analytics.dim_work_item_type(type_key),
    state_key INT NOT NULL REFERENCES analytics.dim_state(state_key),
    active_wip_count INT NOT NULL DEFAULT 0,
    completed_throughput_count INT NOT NULL DEFAULT 0,
    created_count INT NOT NULL DEFAULT 0,
    avg_cycle_time_days NUMERIC(10,2) DEFAULT 0,
    avg_lead_time_days NUMERIC(10,2) DEFAULT 0,
    total_story_points_completed NUMERIC(10,2) DEFAULT 0,
    snapshot_generated_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_fact_daily_flow PRIMARY KEY (snapshot_date_key, project_key, type_key, state_key)
);

-- -----------------------------------------------------------------------------
-- 3. VISTAS ANALÍTICAS PARA POWER BI WEB
-- -----------------------------------------------------------------------------

-- Vista de Throughput Semanal (Entregas por Semana)
CREATE OR REPLACE VIEW analytics.vw_throughput_weekly AS
SELECT
    d.year,
    d.week_of_year,
    MIN(d.full_date) AS week_start_date,
    p.project_name,
    t.work_item_type,
    COUNT(f.work_item_id) AS throughput_count,
    SUM(COALESCE(f.story_points, 0)) AS total_story_points,
    ROUND(AVG(f.cycle_time_days), 2) AS avg_cycle_time_days,
    ROUND(AVG(f.lead_time_days), 2) AS avg_lead_time_days,
    PERCENTILE_CONT(0.85) WITHIN GROUP (ORDER BY f.cycle_time_days) AS p85_cycle_time_days
FROM analytics.fact_work_items f
JOIN analytics.dim_date d ON f.closed_date_key = d.date_key
JOIN analytics.dim_project p ON f.project_key = p.project_key
JOIN analytics.dim_work_item_type t ON f.type_key = t.type_key
WHERE f.is_closed = TRUE AND f.closed_date_key <> -1
GROUP BY d.year, d.week_of_year, p.project_name, t.work_item_type;

-- Vista de Resumen de Métricas de Flujo (Kanban / Scrum Dashboard)
CREATE OR REPLACE VIEW analytics.vw_flow_metrics_summary AS
SELECT
    f.work_item_id,
    f.title,
    p.project_name,
    t.work_item_type,
    t.category AS type_category,
    s.state_name,
    s.state_category,
    m_ass.display_name AS assigned_to,
    m_cre.display_name AS created_by,
    i.sprint_name,
    a.area_path,
    f.created_date_utc,
    f.activated_date_utc,
    f.closed_date_utc,
    f.lead_time_days,
    f.cycle_time_days,
    f.queue_time_days,
    f.wip_age_days,
    f.story_points,
    f.priority,
    f.severity,
    f.tags,
    f.is_closed,
    f.is_active
FROM analytics.fact_work_items f
JOIN analytics.dim_project p ON f.project_key = p.project_key
JOIN analytics.dim_work_item_type t ON f.type_key = t.type_key
JOIN analytics.dim_state s ON f.state_key = s.state_key
JOIN analytics.dim_iteration i ON f.iteration_key = i.iteration_key
JOIN analytics.dim_area a ON f.area_key = a.area_key
JOIN analytics.dim_member m_ass ON f.assigned_to_key = m_ass.member_key
JOIN analytics.dim_member m_cre ON f.created_by_key = m_cre.member_key;

