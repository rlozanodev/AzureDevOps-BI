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
            f.work_item_id,
            f.rev,
            f.title,
            p.project_name,
            t.work_item_type,
            t.category AS work_item_category,
            s.state_name,
            s.state_category,
            i.iteration_path,
            i.sprint_name,
            a.area_path,
            m_ass.display_name AS assigned_to,
            m_cre.display_name AS created_by,
            f.created_date_utc,
            f.activated_date_utc,
            f.closed_date_utc,
            f.changed_date_utc,
            f.lead_time_days,
            f.cycle_time_days,
            f.queue_time_days,
            f.wip_age_days,
            f.is_closed,
            f.is_active,
            f.story_points,
            f.original_estimate,
            f.remaining_work,
            f.completed_work,
            f.priority,
            f.severity,
            f.tags,
            f.last_transformed_at_utc
        FROM pg.analytics.fact_work_items f
        LEFT JOIN pg.analytics.dim_project p ON f.project_key = p.project_key
        LEFT JOIN pg.analytics.dim_work_item_type t ON f.type_key = t.type_key
        LEFT JOIN pg.analytics.dim_state s ON f.state_key = s.state_key
        LEFT JOIN pg.analytics.dim_iteration i ON f.iteration_key = i.iteration_key
        LEFT JOIN pg.analytics.dim_area a ON f.area_key = a.area_key
        LEFT JOIN pg.analytics.dim_member m_ass ON f.assigned_to_key = m_ass.member_key
        LEFT JOIN pg.analytics.dim_member m_cre ON f.created_by_key = m_cre.member_key
        WHERE p.project_name = '{project_name.replace("'", "''")}'
        ORDER BY f.work_item_id ASC
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
