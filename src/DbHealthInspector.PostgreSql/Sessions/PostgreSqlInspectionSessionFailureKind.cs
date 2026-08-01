namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The closed classification of sanitized inspection-session failures, by the stage that failed.
/// </summary>
internal enum PostgreSqlInspectionSessionFailureKind
{
    /// <summary>
    /// Beginning the transaction, or running B001/B002, failed.
    /// </summary>
    InitializationFailed,

    /// <summary>
    /// B003 failed, or the effective state it reported did not match every required condition.
    /// </summary>
    VerificationFailed,

    /// <summary>
    /// The authorized operation failed with an expected PostgreSQL/Npgsql error.
    /// </summary>
    ExecutionFailed,

    /// <summary>
    /// Rollback or disposal failed while no earlier failure was already propagating.
    /// </summary>
    CleanupFailed,
}
