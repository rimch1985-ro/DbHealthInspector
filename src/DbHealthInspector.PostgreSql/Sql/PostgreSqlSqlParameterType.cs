namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed set of parameter types the productive inventory binds. GC-DHI-04B needs only
/// 32-bit integers (the three millisecond timeout values); no general-purpose conversion system
/// is introduced.
/// </summary>
internal enum PostgreSqlSqlParameterType
{
    /// <summary>
    /// A 32-bit signed integer, bound as <c>NpgsqlDbType.Integer</c>.
    /// </summary>
    Int32,
}
