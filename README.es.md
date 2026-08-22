# DbHealth Inspector

DbHealth Inspector es una utilidad open source de línea de comandos para realizar
diagnósticos seguros y reproducibles sobre metadatos de bases PostgreSQL.

> **Estado actual:** `dbhealth inspect postgresql` inspecciona una base de datos
> real y muestra los hallazgos DBH001-DBH005 en consola. Los reportes JSON y en
> archivo todavía no están disponibles.

## Límites del producto

- PostgreSQL 15-18 es el rango soportado inicialmente.
- Toda inspección se ejecuta de forma explícitamente read-only.
- v0.1.0 inspecciona solamente metadatos y estadísticas permitidas.
- No se consultan filas de tablas empresariales.
- Los hallazgos incluyen evidencia y recomendaciones no destructivas.
- La herramienta nunca aplica reparaciones automáticas, DDL ni DML.

## Inicio rápido

```bash
export DBHEALTH_CONNECTION="Host=localhost;Port=5432;Database=mydb;Username=inspector;Password=..."
dbhealth inspect postgresql
```

Entrega la conexión por variable de entorno, no por línea de comandos: un valor
pasado con `--connection` puede quedar visible en el historial del shell y en el
listado de procesos. `--connection-env <NOMBRE>` lee la variable que elijas.

El comando informa qué tablas no tienen clave primaria, cuáles superaron un
umbral de tamaño o de filas, qué índices son duplicados estructurales exactos,
qué índices grandes no registran lecturas y qué índices marcó el motor como
inválidos.

### Comandos

```text
dbhealth
└── inspect
    └── postgresql
```

### Opciones

| Opción | Significado |
|---|---|
| `--connection <CADENA>` | Cadena de conexión. Visible en el historial; prefiere las alternativas siguientes. |
| `--connection-env <NOMBRE>` | Nombre de una variable de entorno con la cadena de conexión. |
| `--large-table-row-threshold <N>` | Umbral de filas de DBH002. Predeterminado `1000000`. |
| `--large-table-size-threshold-mb <N>` | Umbral de tamaño de DBH002. Predeterminado `1024`. |
| `--unused-index-size-threshold-mb <N>` | Tamaño mínimo de índice para DBH004. Predeterminado `10`. |

La precedencia de conexión es `--connection`, luego la variable indicada por
`--connection-env` y por último `DBHEALTH_CONNECTION`. Nombrar una variable que
no existe o está vacía provoca un fallo, no un retroceso a la predeterminada.

**Las opciones `-mb` usan unidades binarias: una unidad equivale exactamente a
1.048.576 bytes.** Así, `--large-table-size-threshold-mb 1024` son exactamente
1.073.741.824 bytes y `--unused-index-size-threshold-mb 10` son exactamente
10.485.760 bytes, es decir los valores predeterminados.

### Códigos de salida

```text
0 = finalizó sin hallazgos, o solo con hallazgos Info
1 = finalizó con al menos un hallazgo Warning o Critical
2 = fallo de uso, configuración, conexión o inspección
```

### Cómo leer un resultado limpio

Cuando no se reporta nada, el comando lo dice de forma explícita y aclara qué
significa: no se detectaron problemas **con los diagnósticos habilitados**. Que
cinco reglas estructurales no encuentren nada no garantiza que la base de datos
no tenga otros problemas.

Las cadenas de conexión, contraseñas, hosts y nombres de usuario nunca se
imprimen, en ninguna ruta, incluida la salida de error.

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
