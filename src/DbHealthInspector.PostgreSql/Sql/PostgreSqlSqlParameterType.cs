namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed set of parameter types the productive inventory binds. GC-DHI-04D freezes it at
/// exactly two: the three millisecond timeout values and the two schema-filter arrays. No
/// general-purpose conversion system is introduced.
/// </summary>
internal enum PostgreSqlSqlParameterType
{
    /// <summary>
    /// A 32-bit signed integer, bound as <c>NpgsqlDbType.Integer</c>.
    /// </summary>
    Int32,

    /// <summary>
    /// An ordered, non-null array of text values, bound as
    /// <c>NpgsqlDbType.Array | NpgsqlDbType.Text</c>. Used by the two schema-filter parameters of
    /// D001, E001 and E002; an empty array is valid and means "no filter of that kind".
    /// </summary>
    TextArray,
}
