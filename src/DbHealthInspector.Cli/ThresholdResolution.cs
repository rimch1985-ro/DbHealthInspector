using System.Globalization;
using DbHealthInspector.Core.Rules;

namespace DbHealthInspector.Cli;

/// <summary>
/// The outcome of turning the three optional threshold options into a
/// <see cref="DiagnosticThresholds"/>.
/// </summary>
internal sealed record ThresholdResolution
{
    private ThresholdResolution(DiagnosticThresholds? thresholds, string? error)
    {
        Thresholds = thresholds;
        Error = error;
    }

    /// <summary>The resolved thresholds, or <see langword="null"/> on failure.</summary>
    internal DiagnosticThresholds? Thresholds { get; }

    /// <summary>The fixed failure message, or <see langword="null"/> on success.</summary>
    internal string? Error { get; }

    internal bool Succeeded => Thresholds is not null;

    /// <summary>
    /// Bytes in one unit of the historically approved <c>-mb</c> options. Binary, not decimal:
    /// this exact factor is what makes both byte defaults reproducible from the command line as
    /// <c>1024</c> and <c>10</c> (§8.1).
    /// </summary>
    internal const long BytesPerMegabyte = 1_048_576L;

    /// <summary>
    /// Resolves thresholds from raw option text. Each argument is <see langword="null"/> when the
    /// option was not supplied.
    /// </summary>
    /// <remarks>
    /// When no option is supplied, <see cref="DiagnosticThresholds.Default"/> is returned
    /// directly — it is not reconstructed from its own values.
    /// <para>
    /// The values are parsed here rather than by System.CommandLine so that every rejection
    /// produces this CLI's own fixed message, and so no offending token is echoed.
    /// </para>
    /// </remarks>
    internal static ThresholdResolution Resolve(
        string? largeTableRowThreshold,
        string? largeTableSizeThresholdMegabytes,
        string? unusedIndexSizeThresholdMegabytes)
    {
        if (largeTableRowThreshold is null
            && largeTableSizeThresholdMegabytes is null
            && unusedIndexSizeThresholdMegabytes is null)
        {
            return new ThresholdResolution(DiagnosticThresholds.Default, null);
        }

        DiagnosticThresholds defaults = DiagnosticThresholds.Default;

        if (!TryResolveRows(largeTableRowThreshold, defaults.LargeTableRowThreshold, out long rows))
        {
            return Invalid();
        }

        if (!TryResolveMegabytes(
                largeTableSizeThresholdMegabytes, defaults.LargeTableSizeThresholdBytes, out long tableBytes))
        {
            return Invalid();
        }

        if (!TryResolveMegabytes(
                unusedIndexSizeThresholdMegabytes, defaults.UnusedIndexSizeThresholdBytes, out long indexBytes))
        {
            return Invalid();
        }

        return new ThresholdResolution(new DiagnosticThresholds(rows, tableBytes, indexBytes), null);
    }

    private static ThresholdResolution Invalid() =>
        new(null, CliMessages.InvalidThresholdValue);

    /// <summary>Row counts are used exactly as supplied; no conversion applies.</summary>
    private static bool TryResolveRows(string? text, long fallback, out long value)
    {
        if (text is null)
        {
            value = fallback;
            return true;
        }

        return TryParsePositive(text, out value);
    }

    /// <summary>
    /// Converts a binary-megabyte option to bytes with a checked multiplication. Overflow is a
    /// rejection, never an <see cref="OverflowException"/> reaching the user.
    /// </summary>
    private static bool TryResolveMegabytes(string? text, long fallback, out long bytes)
    {
        if (text is null)
        {
            bytes = fallback;
            return true;
        }

        if (!TryParsePositive(text, out long megabytes))
        {
            bytes = 0;
            return false;
        }

        try
        {
            bytes = checked(megabytes * BytesPerMegabyte);
            return true;
        }
        catch (OverflowException)
        {
            bytes = 0;
            return false;
        }
    }

    private static bool TryParsePositive(string text, out long value) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}
