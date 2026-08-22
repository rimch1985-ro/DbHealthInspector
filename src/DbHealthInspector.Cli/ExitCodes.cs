namespace DbHealthInspector.Cli;

/// <summary>
/// The three exit codes frozen by GC-DHI-05B_DEFINITION.md §13. No fourth value exists.
/// </summary>
internal static class ExitCodes
{
    /// <summary>Inspection completed with no findings, or with Info-only findings.</summary>
    internal const int Success = 0;

    /// <summary>Inspection completed and contains at least one Warning or Critical finding.</summary>
    internal const int FindingsPresent = 1;

    /// <summary>The command or the inspection could not be considered successfully completed.</summary>
    internal const int Failure = 2;
}
