#!/usr/bin/env python3
"""
Azure DevOps Analytics OLAP Transformation Engine
Powered by DuckDB + PostgreSQL
Extracts from staging.raw_work_items and loads into analytics star schema
Calculates Flow Metrics: Lead Time, Cycle Time, Queue Time, WIP Age, Throughput
"""

import os
import sys
import time
import logging
from datetime import datetime, timezone
from pathlib import Path
from dotenv import load_dotenv
import duckdb
import psycopg2

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] [DuckDB-OLAP] %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S"
)
logger = logging.getLogger("AnalyticsEngine")

# Load environment variables
env_paths = [
    Path(__file__).resolve().parent.parent / ".env",
    Path(__file__).resolve().parent / ".env",
    Path.cwd() / ".env"
]
for env_path in env_paths:
    if env_path.exists():
        load_dotenv(dotenv_path=env_path)
        logger.info(f"Loaded environment configuration from {env_path}")
        break

def get_db_config():
    # Parse ConnectionStrings__PostgresDb if present
    cs = os.getenv("ConnectionStrings__PostgresDb", "")
    host = os.getenv("DB_HOST", "localhost")
    port = os.getenv("DB_PORT", "5432")
    dbname = os.getenv("POSTGRES_DB", "azure_devops_dw")
    user = os.getenv("POSTGRES_USER", "postgres")
    password = os.getenv("POSTGRES_PASSWORD", "postgres_secure_password_123!")

    if cs:
        parts = dict(item.split("=", 1) for item in cs.split(";") if "=" in item)
        host = parts.get("Host", host)
        port = parts.get("Port", port)
        dbname = parts.get("Database", dbname)
        user = parts.get("Username", user)
        password = parts.get("Password", password)

    return {
        "host": host,
        "port": port,
        "dbname": dbname,
        "user": user,
        "password": password
    }

def run_transformations():
    start_time = time.time()
    db_cfg = get_db_config()
    logger.info(f"Connecting DuckDB to PostgreSQL at {db_cfg['host']}:{db_cfg['port']}/{db_cfg['dbname']}...")

    # Open direct connection with psycopg2 for DDL / transactional operations if needed
    pg_conn = psycopg2.connect(
        host=db_cfg["host"],
        port=db_cfg["port"],
        dbname=db_cfg["dbname"],
        user=db_cfg["user"],
        password=db_cfg["password"]
    )
    pg_conn.autocommit = True
    pg_cur = pg_conn.cursor()

    # Initialize DuckDB in-memory session
    con = duckdb.connect()
    con.execute("INSTALL postgres;")
    con.execute("LOAD postgres;")

    attach_sql = (
        f"ATTACH 'host={db_cfg['host']} port={db_cfg['port']} "
        f"dbname={db_cfg['dbname']} user={db_cfg['user']} password={db_cfg['password']}' "
        f"AS pg (TYPE POSTGRES);"
    )
    con.execute(attach_sql)
    logger.info("Successfully attached PostgreSQL database into DuckDB session.")

    # Check staging records count
    raw_count = con.execute("SELECT COUNT(*) FROM pg.staging.raw_work_items;").fetchone()[0]
    logger.info(f"Total raw work items in staging: {raw_count}")

    if raw_count == 0:
        logger.info("Staging table is currently empty. Ensuring baseline dimension values...")

    # -------------------------------------------------------------------------
    # 1. POPULATE DIMENSIONS
    # -------------------------------------------------------------------------
    logger.info("Step 1/3: Populating Dimension tables (dim_project, dim_work_item_type, dim_state, dim_iteration, dim_area, dim_member)...")

    # 1.1 dim_project
    pg_cur.execute("""
        INSERT INTO analytics.dim_project (project_name)
        SELECT DISTINCT project_name
        FROM staging.raw_work_items
        WHERE project_name IS NOT NULL AND project_name <> ''
        ON CONFLICT (project_name) DO NOTHING;
    """)

    # 1.2 dim_work_item_type
    pg_cur.execute("""
        INSERT INTO analytics.dim_work_item_type (work_item_type, category)
        SELECT DISTINCT
            work_item_type,
            CASE
                WHEN LOWER(work_item_type) IN ('epic') THEN 'Epic'
                WHEN LOWER(work_item_type) IN ('feature') THEN 'Feature'
                WHEN LOWER(work_item_type) IN ('user story', 'product backlog item', 'requirement') THEN 'Requirement'
                WHEN LOWER(work_item_type) IN ('bug', 'defect', 'issue') THEN 'Bug'
                WHEN LOWER(work_item_type) IN ('task', 'sub-task') THEN 'Task'
                ELSE 'Requirement'
            END AS category
        FROM staging.raw_work_items
        WHERE work_item_type IS NOT NULL AND work_item_type <> ''
        ON CONFLICT (work_item_type) DO UPDATE SET
            category = EXCLUDED.category;
    """)

    # 1.3 dim_state
    pg_cur.execute("""
        INSERT INTO analytics.dim_state (state_name, state_category)
        SELECT DISTINCT
            state AS state_name,
            CASE
                WHEN LOWER(state) IN ('new', 'proposed', 'to do', 'backlog', 'open') THEN 'Proposed'
                WHEN LOWER(state) IN ('active', 'in progress', 'committed', 'in development', 'doing') THEN 'In Progress'
                WHEN LOWER(state) IN ('resolved', 'done', 'closed', 'completed', 'verified') THEN 'Completed'
                WHEN LOWER(state) IN ('removed', 'cancelled', 'rejected') THEN 'Removed'
                ELSE 'In Progress'
            END AS state_category
        FROM staging.raw_work_items
        WHERE state IS NOT NULL AND state <> ''
        ON CONFLICT (state_name) DO UPDATE SET
            state_category = EXCLUDED.state_category;
    """)

    # 1.4 dim_iteration
    pg_cur.execute("""
        INSERT INTO analytics.dim_iteration (iteration_path, sprint_name)
        SELECT DISTINCT
            iteration_path,
            COALESCE(NULLIF(SPLIT_PART(iteration_path, '\\', ARRAY_LENGTH(STRING_TO_ARRAY(iteration_path, '\\'), 1)), ''), iteration_path) AS sprint_name
        FROM staging.raw_work_items
        WHERE iteration_path IS NOT NULL AND iteration_path <> ''
        ON CONFLICT (iteration_path) DO NOTHING;
    """)

    # 1.5 dim_area
    pg_cur.execute("""
        INSERT INTO analytics.dim_area (area_path)
        SELECT DISTINCT area_path
        FROM staging.raw_work_items
        WHERE area_path IS NOT NULL AND area_path <> ''
        ON CONFLICT (area_path) DO NOTHING;
    """)

    # 1.6 dim_member
    pg_cur.execute("""
        INSERT INTO analytics.dim_member (unique_name, display_name)
        SELECT DISTINCT
            COALESCE(assigned_to_unique_name, assigned_to_name) AS unique_name,
            assigned_to_name AS display_name
        FROM staging.raw_work_items
        WHERE assigned_to_name IS NOT NULL AND assigned_to_name <> ''
        UNION
        SELECT DISTINCT
            COALESCE(created_by_unique_name, created_by_name) AS unique_name,
            created_by_name AS display_name
        FROM staging.raw_work_items
        WHERE created_by_name IS NOT NULL AND created_by_name <> ''
        ON CONFLICT (unique_name) DO UPDATE SET
            display_name = EXCLUDED.display_name;
    """)

    logger.info("Dimensions successfully refreshed.")

    # -------------------------------------------------------------------------
    # 2. VECTORIZED FLOW METRICS & FACT WORK ITEMS TRANSFORMATION
    # -------------------------------------------------------------------------
    logger.info("Step 2/3: Calculating vectorized flow metrics and loading fact_work_items...")

    if raw_count > 0:
        # Perform vectorized OLAP calculation and upsert into analytics.fact_work_items
        fact_upsert_sql = """
        INSERT INTO analytics.fact_work_items (
            work_item_id,
            rev,
            title,
            project_key,
            type_key,
            state_key,
            iteration_key,
            area_key,
            assigned_to_key,
            created_by_key,
            created_date_key,
            activated_date_key,
            closed_date_key,
            changed_date_key,
            created_date_utc,
            activated_date_utc,
            closed_date_utc,
            changed_date_utc,
            lead_time_days,
            cycle_time_days,
            queue_time_days,
            wip_age_days,
            is_closed,
            is_active,
            story_points,
            original_estimate,
            remaining_work,
            completed_work,
            priority,
            severity,
            tags,
            last_transformed_at_utc
        )
        SELECT
            r.id AS work_item_id,
            r.rev,
            r.title,
            COALESCE(p.project_key, 1) AS project_key,
            COALESCE(t.type_key, 1) AS type_key,
            COALESCE(s.state_key, 1) AS state_key,
            COALESCE(i.iteration_key, 1) AS iteration_key,
            COALESCE(a.area_key, 1) AS area_key,
            COALESCE(m_ass.member_key, 1) AS assigned_to_key,
            COALESCE(m_cre.member_key, 1) AS created_by_key,
            COALESCE(TO_CHAR(r.created_date, 'YYYYMMDD')::INT, -1) AS created_date_key,
            COALESCE(TO_CHAR(r.activated_date, 'YYYYMMDD')::INT, -1) AS activated_date_key,
            COALESCE(TO_CHAR(r.closed_date, 'YYYYMMDD')::INT, -1) AS closed_date_key,
            COALESCE(TO_CHAR(r.changed_date, 'YYYYMMDD')::INT, -1) AS changed_date_key,
            r.created_date AS created_date_utc,
            r.activated_date AS activated_date_utc,
            r.closed_date AS closed_date_utc,
            r.changed_date AS changed_date_utc,
            -- Lead Time: Tiempo total desde creación hasta cierre (días)
            CASE
                WHEN r.closed_date IS NOT NULL AND r.created_date IS NOT NULL AND r.closed_date >= r.created_date
                    THEN ROUND(EXTRACT(EPOCH FROM (r.closed_date - r.created_date)) / 86400.0, 2)
                ELSE NULL
            END AS lead_time_days,
            -- Cycle Time: Tiempo activo de trabajo desde activación hasta cierre (días)
            CASE
                WHEN r.closed_date IS NOT NULL AND r.activated_date IS NOT NULL AND r.closed_date >= r.activated_date
                    THEN ROUND(EXTRACT(EPOCH FROM (r.closed_date - r.activated_date)) / 86400.0, 2)
                WHEN r.closed_date IS NOT NULL AND r.created_date IS NOT NULL AND r.closed_date >= r.created_date
                    THEN ROUND(EXTRACT(EPOCH FROM (r.closed_date - r.created_date)) / 86400.0, 2)
                ELSE NULL
            END AS cycle_time_days,
            -- Queue Time: Tiempo en cola antes de ser activado (días)
            CASE
                WHEN r.activated_date IS NOT NULL AND r.created_date IS NOT NULL AND r.activated_date >= r.created_date
                    THEN ROUND(EXTRACT(EPOCH FROM (r.activated_date - r.created_date)) / 86400.0, 2)
                ELSE NULL
            END AS queue_time_days,
            -- WIP Age: Antigüedad de ítems actualmente en curso (días)
            CASE
                WHEN (s.state_category = 'In Progress' OR (r.closed_date IS NULL AND s.state_category <> 'Removed'))
                    THEN ROUND(EXTRACT(EPOCH FROM (CURRENT_TIMESTAMP - COALESCE(r.activated_date, r.created_date))) / 86400.0, 2)
                ELSE NULL
            END AS wip_age_days,
            -- Indicadores booleanos
            CASE WHEN s.state_category = 'Completed' OR r.closed_date IS NOT NULL THEN TRUE ELSE FALSE END AS is_closed,
            CASE WHEN s.state_category = 'In Progress' AND r.closed_date IS NULL THEN TRUE ELSE FALSE END AS is_active,
            r.story_points,
            r.original_estimate,
            r.remaining_work,
            r.completed_work,
            r.priority,
            r.severity,
            r.tags,
            CURRENT_TIMESTAMP AS last_transformed_at_utc
        FROM staging.raw_work_items r
        LEFT JOIN analytics.dim_project p ON r.project_name = p.project_name
        LEFT JOIN analytics.dim_work_item_type t ON r.work_item_type = t.work_item_type
        LEFT JOIN analytics.dim_state s ON r.state = s.state_name
        LEFT JOIN analytics.dim_iteration i ON r.iteration_path = i.iteration_path
        LEFT JOIN analytics.dim_area a ON r.area_path = a.area_path
        LEFT JOIN analytics.dim_member m_ass ON COALESCE(r.assigned_to_unique_name, r.assigned_to_name) = m_ass.unique_name
        LEFT JOIN analytics.dim_member m_cre ON COALESCE(r.created_by_unique_name, r.created_by_name) = m_cre.unique_name
        ON CONFLICT (work_item_id) DO UPDATE SET
            rev = EXCLUDED.rev,
            title = EXCLUDED.title,
            project_key = EXCLUDED.project_key,
            type_key = EXCLUDED.type_key,
            state_key = EXCLUDED.state_key,
            iteration_key = EXCLUDED.iteration_key,
            area_key = EXCLUDED.area_key,
            assigned_to_key = EXCLUDED.assigned_to_key,
            created_by_key = EXCLUDED.created_by_key,
            created_date_key = EXCLUDED.created_date_key,
            activated_date_key = EXCLUDED.activated_date_key,
            closed_date_key = EXCLUDED.closed_date_key,
            changed_date_key = EXCLUDED.changed_date_key,
            created_date_utc = EXCLUDED.created_date_utc,
            activated_date_utc = EXCLUDED.activated_date_utc,
            closed_date_utc = EXCLUDED.closed_date_utc,
            changed_date_utc = EXCLUDED.changed_date_utc,
            lead_time_days = EXCLUDED.lead_time_days,
            cycle_time_days = EXCLUDED.cycle_time_days,
            queue_time_days = EXCLUDED.queue_time_days,
            wip_age_days = EXCLUDED.wip_age_days,
            is_closed = EXCLUDED.is_closed,
            is_active = EXCLUDED.is_active,
            story_points = EXCLUDED.story_points,
            original_estimate = EXCLUDED.original_estimate,
            remaining_work = EXCLUDED.remaining_work,
            completed_work = EXCLUDED.completed_work,
            priority = EXCLUDED.priority,
            severity = EXCLUDED.severity,
            tags = EXCLUDED.tags,
            last_transformed_at_utc = EXCLUDED.last_transformed_at_utc;
        """
        pg_cur.execute(fact_upsert_sql)
        logger.info("analytics.fact_work_items updated successfully.")

    # -------------------------------------------------------------------------
    # 3. AGGREGATE DAILY FLOW SNAPSHOT & THROUGHPUT
    # -------------------------------------------------------------------------
    logger.info("Step 3/3: Generating fact_daily_flow_snapshot...")

    if raw_count > 0:
        pg_cur.execute("""
        INSERT INTO analytics.fact_daily_flow_snapshot (
            snapshot_date_key,
            project_key,
            type_key,
            state_key,
            active_wip_count,
            completed_throughput_count,
            created_count,
            avg_cycle_time_days,
            avg_lead_time_days,
            total_story_points_completed,
            snapshot_generated_at_utc
        )
        SELECT
            d.date_key AS snapshot_date_key,
            f.project_key,
            f.type_key,
            f.state_key,
            SUM(CASE WHEN f.is_active = TRUE THEN 1 ELSE 0 END) AS active_wip_count,
            SUM(CASE WHEN f.is_closed = TRUE AND f.closed_date_key = d.date_key THEN 1 ELSE 0 END) AS completed_throughput_count,
            SUM(CASE WHEN f.created_date_key = d.date_key THEN 1 ELSE 0 END) AS created_count,
            COALESCE(ROUND(AVG(CASE WHEN f.is_closed = TRUE AND f.closed_date_key = d.date_key THEN f.cycle_time_days END), 2), 0) AS avg_cycle_time_days,
            COALESCE(ROUND(AVG(CASE WHEN f.is_closed = TRUE AND f.closed_date_key = d.date_key THEN f.lead_time_days END), 2), 0) AS avg_lead_time_days,
            COALESCE(SUM(CASE WHEN f.is_closed = TRUE AND f.closed_date_key = d.date_key THEN f.story_points ELSE 0 END), 0) AS total_story_points_completed,
            CURRENT_TIMESTAMP AS snapshot_generated_at_utc
        FROM analytics.dim_date d
        CROSS JOIN (
            SELECT DISTINCT project_key, type_key, state_key FROM analytics.fact_work_items
        ) dims
        JOIN analytics.fact_work_items f 
          ON f.project_key = dims.project_key 
         AND f.type_key = dims.type_key 
         AND f.state_key = dims.state_key
        WHERE d.full_date >= CURRENT_DATE - INTERVAL '90 days'
          AND d.full_date <= CURRENT_DATE
        GROUP BY d.date_key, f.project_key, f.type_key, f.state_key
        ON CONFLICT (snapshot_date_key, project_key, type_key, state_key) DO UPDATE SET
            active_wip_count = EXCLUDED.active_wip_count,
            completed_throughput_count = EXCLUDED.completed_throughput_count,
            created_count = EXCLUDED.created_count,
            avg_cycle_time_days = EXCLUDED.avg_cycle_time_days,
            avg_lead_time_days = EXCLUDED.avg_lead_time_days,
            total_story_points_completed = EXCLUDED.total_story_points_completed,
            snapshot_generated_at_utc = EXCLUDED.snapshot_generated_at_utc;
        """)
        logger.info("analytics.fact_daily_flow_snapshot updated successfully.")

    # Close connections
    con.close()
    pg_cur.close()
    pg_conn.close()

    elapsed = time.time() - start_time
    logger.info(f"Transformation Pipeline finished successfully in {elapsed:.2f} seconds.")

if __name__ == "__main__":
    try:
        run_transformations()
        sys.exit(0)
    except Exception as e:
        logger.exception(f"Fatal error in DuckDB OLAP Transformation: {e}")
        sys.exit(1)
