using Npgsql;

namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// Opens a connection from an already-built <see cref="NpgsqlDataSource"/>. This is the seam
/// <see cref="PostgreSqlConnectionFactory"/> calls from <see cref="PostgreSqlConnectionFactory
/// .OpenConnectionAsync"/> — genuinely part of the production call path, not a test-only
/// abstraction — so that the factory's cancellation and exception-sanitization logic around the
/// call can be exercised deterministically, without a real PostgreSQL server, by substituting a
/// fake delegate in tests. See docs/design/postgresql-connection-boundary.md §5.
/// </summary>
internal delegate ValueTask<NpgsqlConnection> PostgreSqlConnectionOpener(
    NpgsqlDataSource dataSource, CancellationToken cancellationToken);

/// <summary>
/// The only production implementation of <see cref="PostgreSqlConnectionOpener"/>: it does
/// nothing but delegate to <see cref="NpgsqlDataSource.OpenConnectionAsync(CancellationToken)"/>.
/// </summary>
internal static class NpgsqlDataSourceConnectionOpener
{
    internal static readonly PostgreSqlConnectionOpener Default =
        static (dataSource, cancellationToken) => dataSource.OpenConnectionAsync(cancellationToken);
}
