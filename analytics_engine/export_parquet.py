#!/usr/bin/env python3
import sys
import os
import time
import logging
from pathlib import Path
from dotenv import load_dotenv
import duckdb

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] [DuckDB-Export] %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S"
)
logger = logging.getLogger("ExportParquet")

def get_db_config():
    env_paths = [
        Path(__file__).resolve().parent.parent / ".env",
        Path(__file__).resolve().parent / ".env",
        Path.cwd() / ".env"
    ]
    for env_path in env_paths:
        if env_path.exists():
            load_dotenv(dotenv_path=env_path)
            break

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

    return host, port, dbname, user, password

def export_parquet(project_name, output_path):
    start_time = time.time()
    logger.info(f"Starting Parquet export for project: '{project_name}'")
    
    host, port, dbname, user, password = get_db_config()
    
    con = duckdb.connect()
    con.execute("INSTALL postgres;")
    con.execute("LOAD postgres;")
    
    attach_sql = (
        f"ATTACH 'host={host} port={port} "
        f"dbname={dbname} user={user} password={password}' "
        f"AS pg (TYPE POSTGRES);"
    )
    con.execute(attach_sql)
    
    export_query = f"""
    COPY (
        SELECT 
            r.id AS work_item_id,
            r.rev,
            r.title,
            r.project_name,
            r.work_item_type,
            CASE
                WHEN LOWER(r.work_item_type) IN ('epic') THEN 'Epic'
                WHEN LOWER(r.work_item_type) IN ('feature') THEN 'Feature'
                WHEN LOWER(r.work_item_type) IN ('user story', 'product backlog item', 'requirement') THEN 'Requirement'
                WHEN LOWER(r.work_item_type) IN ('bug', 'defect', 'issue') THEN 'Bug'
                WHEN LOWER(r.work_item_type) IN ('task', 'sub-task') THEN 'Task'
                ELSE 'Requirement'
            END AS work_item_category,
            r.state AS state_name,
            CASE
                WHEN LOWER(r.state) IN ('new', 'proposed', 'to do', 'backlog', 'open') THEN 'Proposed'
                WHEN LOWER(r.state) IN ('active', 'in progress', 'committed', 'in development', 'doing') THEN 'In Progress'
                WHEN LOWER(r.state) IN ('resolved', 'done', 'closed', 'completed', 'verified') THEN 'Completed'
                WHEN LOWER(r.state) IN ('removed', 'cancelled', 'rejected') THEN 'Removed'
                ELSE 'In Progress'
            END AS state_category,
            r.iteration_path,
            COALESCE(NULLIF(SPLIT_PART(r.iteration_path, '\\', ARRAY_LENGTH(STRING_TO_ARRAY(r.iteration_path, '\\'), 1)), ''), r.iteration_path) AS sprint_name,
            r.area_path,
            COALESCE(r.assigned_to_name, 'Sin Asignar') AS assigned_to,
            COALESCE(r.created_by_name, 'Desconocido') AS created_by,
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
                WHEN ((CASE
                        WHEN LOWER(r.state) IN ('active', 'in progress', 'committed', 'in development', 'doing') THEN 'In Progress'
                        WHEN LOWER(r.state) IN ('resolved', 'done', 'closed', 'completed', 'verified') THEN 'Completed'
                        WHEN LOWER(r.state) IN ('removed', 'cancelled', 'rejected') THEN 'Removed'
                        ELSE 'Proposed' END) = 'In Progress' OR (r.closed_date IS NULL AND (CASE
                        WHEN LOWER(r.state) IN ('removed', 'cancelled', 'rejected') THEN 'Removed'
                        ELSE 'Other' END) <> 'Removed'))
                    THEN ROUND(EXTRACT(EPOCH FROM (CURRENT_TIMESTAMP - COALESCE(r.activated_date, r.created_date))) / 86400.0, 2)
                ELSE NULL
            END AS wip_age_days,
            
            -- Indicadores
            CASE WHEN (CASE
                        WHEN LOWER(r.state) IN ('resolved', 'done', 'closed', 'completed', 'verified') THEN 'Completed'
                        ELSE 'Other' END) = 'Completed' OR r.closed_date IS NOT NULL THEN TRUE ELSE FALSE END AS is_closed,
            CASE WHEN (CASE
                        WHEN LOWER(r.state) IN ('active', 'in progress', 'committed', 'in development', 'doing') THEN 'In Progress'
                        ELSE 'Other' END) = 'In Progress' AND r.closed_date IS NULL THEN TRUE ELSE FALSE END AS is_active,
                        
            r.story_points,
            r.original_estimate,
            r.remaining_work,
            r.completed_work,
            r.priority,
            r.severity,
            r.tags,
            CURRENT_TIMESTAMP AS last_transformed_at_utc
        FROM pg.staging.raw_work_items r
        WHERE r.project_name = '{project_name.replace("'", "''")}'
        ORDER BY r.id ASC
    ) TO '{output_path}' (FORMAT PARQUET, COMPRESSION 'SNAPPY');
    """
    
    logger.info(f"Executing DuckDB COPY TO PARQUET...")
    con.execute(export_query)
    
    con.close()
    elapsed = time.time() - start_time
    logger.info(f"Successfully exported '{project_name}' to {output_path} in {elapsed:.2f} seconds.")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        logger.error("Usage: uv run export_parquet.py <ProjectName> <OutputPath>")
        sys.exit(1)
        
    project_name = sys.argv[1]
    output_path = sys.argv[2]
    
    try:
        export_parquet(project_name, output_path)
    except Exception as e:
        logger.exception("Failed to export parquet file")
        sys.exit(1)
