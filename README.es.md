# DbHealth Inspector

DbHealth Inspector es una utilidad open source de línea de comandos para realizar
diagnósticos seguros y reproducibles sobre metadatos de bases PostgreSQL.

> **Estado actual:** únicamente bootstrap del repositorio. Esta compilación no
> inspecciona bases de datos y todavía no implementa DBH001-DBH005.

## Límites del producto

- PostgreSQL 15-18 es el rango soportado inicialmente.
- Toda inspección se ejecutará de forma explícitamente read-only.
- v0.1.0 inspeccionará solamente metadatos y estadísticas permitidas.
- No se consultarán filas de tablas empresariales.
- Los hallazgos incluirán evidencia y recomendaciones no destructivas.
- La herramienta nunca aplicará reparaciones automáticas, DDL ni DML.

## Comando de bootstrap

El paquete está preparado como herramienta global de .NET con el comando futuro:

```text
dbhealth
```

En esta compuerta solo están disponibles la ayuda y la versión del bootstrap:

```bash
dbhealth --help
```

## Estructura del repositorio

```text
src/
├── DbHealthInspector.Core
├── DbHealthInspector.PostgreSql
└── DbHealthInspector.Cli

tests/
├── DbHealthInspector.UnitTests
└── DbHealthInspector.IntegrationTests
```

## Compilación

El repositorio requiere el SDK de .NET fijado en `global.json`.

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet pack src/DbHealthInspector.Cli --configuration Release --no-build
```

## Gobernanza

El alcance y las reglas de seguridad canónicas se encuentran en:

- [`AGENTS.md`](AGENTS.md)
- [`PROJECT_RULES.md`](docs/agent-governance/PROJECT_RULES.md)
- [`PROJECT_STATE.md`](docs/agent-governance/PROJECT_STATE.md)
- [`AGENT_OPERATING_MODEL.md`](docs/agent-governance/AGENT_OPERATING_MODEL.md)
- [`INITIAL_BACKLOG.md`](docs/backlog/INITIAL_BACKLOG.md)

## Seguridad

Consulta [`SECURITY.md`](SECURITY.md). No publiques contraseñas, cadenas de
conexión ni otros secretos en incidencias, logs, reportes o fixtures de pruebas.

## Contribución

Consulta [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Licencia

Distribuido bajo la [licencia MIT](LICENSE).

English documentation: [`README.md`](README.md).
