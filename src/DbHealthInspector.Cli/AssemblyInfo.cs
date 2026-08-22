using System.Runtime.CompilerServices;

// Exposes the CLI's internal command tree, resolvers and renderer to the unit test project, so
// the exit-code contract, connection precedence, threshold conversion and secret redaction can be
// tested against the exact code path production uses, without making any of it public API.
[assembly: InternalsVisibleTo("DbHealthInspector.UnitTests")]
[assembly: InternalsVisibleTo("DbHealthInspector.IntegrationTests")]
