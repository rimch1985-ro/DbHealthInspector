using System.Runtime.CompilerServices;

// GC-DHI-04A: every type in this assembly's connection boundary is intentionally internal (see
// docs/design/postgresql-connection-boundary.md §5). It is exposed only to the test projects
// that need to exercise it directly, never publicly.
[assembly: InternalsVisibleTo("DbHealthInspector.UnitTests")]
[assembly: InternalsVisibleTo("DbHealthInspector.IntegrationTests")]
