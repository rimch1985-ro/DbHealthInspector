namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// Whether the inspected server's major version is inside the range this product supports.
/// </summary>
/// <remarks>
/// An unsupported version is a reported outcome, not a failure: the probe still returns a
/// complete result describing what it could and could not do.
/// </remarks>
internal enum PostgreSqlVersionSupportStatus
{
    /// <summary>The major version is within 15–18 inclusive.</summary>
    Supported,

    /// <summary>The major version is below 15 or above 18.</summary>
    Unsupported,
}
