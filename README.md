# AzureDevOps-BI: Sistema Headless de Ingesta, Procesamiento y Automatización de Métricas

Plataforma empresarial desacoplada construida en **.NET 8/9**, **PostgreSQL 16**, **Python (DuckDB + uv)** y **Power BI REST API** para la extracción, modelado dimensional y automatización de métricas ágiles y de flujo (Lead Time, Cycle Time, Queue Time, WIP Age, Throughput) desde instancias locales **On-Premise de Azure DevOps Server (TFS 2019)** hacia **Power BI Web**.

## 📖 Documentación Completa y Guía de Uso
Para ver el detalle técnico exhaustivo de decisiones de diseño, flujo de trabajo y configuración para producción, consulta el archivo:
👉 [**ARQUITECTURA_Y_GUIA_DE_USO.md**](ARQUITECTURA_Y_GUIA_DE_USO.md)

---

## 🚀 Inicio Rápido (Quickstart)

### 1. Iniciar Base de Datos y Adminer
```bash
docker compose up -d
```
- PostgreSQL: `localhost:5432` (DB: `azure_devops_dw`)
- Adminer UI: `http://localhost:8080`

### 2. Sincronizar Entorno de Python (DuckDB Engine)
```bash
cd analytics_engine
uv sync
cd ..
```

### 3. Ejecutar Pruebas Unitarias y de Integración
```bash
dotnet test
```

### 4. Configurar Variables de Conexión
Copia `.env.example` a `.env` y configura la URL de tu TFS 2019 (`http://edvwp-tfs19-ap/`), tipo de autenticación NTLM y credenciales de Power BI.

### 5. Iniciar el Worker de Sincronización
```bash
dotnet run --project src/AzureDevOps.IngestionWorker/AzureDevOps.IngestionWorker.csproj
```

---

## 🏛️ Esquemas de Base de Datos
- **`staging`**: Datos crudos y semi-estructurados (`staging.raw_work_items`) con control de marcas de agua para sincronización incremental (`staging.sync_watermarks`).
- **`analytics`**: Modelo en estrella (`dim_date`, `dim_project`, `dim_work_item_type`, `dim_state`, `dim_iteration`, `dim_area`, `dim_member`, `fact_work_items`, `fact_daily_flow_snapshot`) y vistas optimizadas para Power BI Web.
