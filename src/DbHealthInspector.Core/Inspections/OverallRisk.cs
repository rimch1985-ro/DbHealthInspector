namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// The deterministic overall risk classification for one inspection, derived solely from the
/// severities of its accepted findings. See <see cref="OverallRiskCalculator"/> for the exact
/// matrix.
/// </summary>
public enum OverallRisk
{
    /// <summary>
    /// No findings were produced.
    /// </summary>
    None,

    /// <summary>
    /// One or more findings were produced, and all of them are <c>Info</c>.
    /// </summary>
    Low,

    /// <summary>
    /// At least one finding is <c>Warning</c>, and none is <c>Critical</c>.
    /// </summary>
    Medium,

    /// <summary>
    /// At least one finding is <c>Critical</c>.
    /// </summary>
    High,
}
