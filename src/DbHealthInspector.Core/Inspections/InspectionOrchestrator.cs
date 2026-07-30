using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// Runs one inspection: captures exactly one <see cref="DatabaseSnapshot"/>, evaluates every
/// enabled rule whose required capabilities are available, and assembles a coherent, immutable
/// <see cref="InspectionResult"/>.
/// </summary>
/// <remarks>
/// See docs/design/inspection-orchestration.md for the full design rationale, ordering
/// guarantees, failure semantics and cancellation semantics.
/// </remarks>
public sealed class InspectionOrchestrator
{
    private const string UnhandledRuleExceptionMessage = "The diagnostic rule failed during evaluation.";
    private const string RuleContractViolationMessage = "The diagnostic rule returned an invalid result.";

    private readonly IDatabaseSnapshotProvider _snapshotProvider;
    private readonly IReadOnlyList<InspectionRuleRegistration> _registrations;

    /// <summary>
    /// Creates an orchestrator for a fixed set of enabled rules.
    /// </summary>
    /// <param name="snapshotProvider">Captures the snapshot each inspection evaluates.</param>
    /// <param name="ruleRegistrations">
    /// The enabled rules. Copied defensively. Cannot be <see langword="null"/> or contain a
    /// <see langword="null"/> registration. No two registrations may share the same
    /// <see cref="IInspectionRule.Code"/>. Each registration's <see cref="IInspectionRule.Code"/>
    /// and <see cref="IInspectionRule.Version"/> must be non-null, its
    /// <see cref="IInspectionRule.Name"/> must be non-blank, and its
    /// <see cref="IInspectionRule.Category"/> must be a defined <see cref="FindingCategory"/>
    /// value. An empty collection is valid: it means no rule is enabled.
    /// </param>
    public InspectionOrchestrator(
        IDatabaseSnapshotProvider snapshotProvider,
        IReadOnlyCollection<InspectionRuleRegistration> ruleRegistrations)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        _snapshotProvider = snapshotProvider;

        IReadOnlyList<InspectionRuleRegistration> copy = Guard.CopyDefensivelyRejectingNullElements(
            ruleRegistrations, nameof(ruleRegistrations));

        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (InspectionRuleRegistration registration in copy)
        {
            IInspectionRule rule = registration.Rule;

            if (rule.Code is null)
            {
                throw new ArgumentException(
                    "A registered rule's Code cannot be null.", nameof(ruleRegistrations));
            }

            if (rule.Version is null)
            {
                throw new ArgumentException(
                    "A registered rule's Version cannot be null.", nameof(ruleRegistrations));
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                throw new ArgumentException(
                    "A registered rule's Name cannot be null, empty or whitespace.", nameof(ruleRegistrations));
            }

            Guard.AgainstUndefinedEnum(rule.Category, nameof(ruleRegistrations));

            if (!seenCodes.Add(rule.Code.Value))
            {
                throw new ArgumentException(
                    $"Duplicate finding code '{rule.Code.Value}' across rule registrations.",
                    nameof(ruleRegistrations));
            }
        }

        _registrations = copy;
    }

    /// <summary>
    /// Runs one inspection.
    /// </summary>
    /// <param name="cancellationToken">
    /// Observed before capturing the snapshot, after capturing it, and before and after every
    /// rule. When cancellation is detected, <see cref="OperationCanceledException"/> propagates
    /// immediately; no partial result is returned and no further rule runs.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The snapshot provider returned a <see langword="null"/> snapshot.
    /// </exception>
    public async Task<InspectionResult> InspectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DatabaseSnapshot? snapshot = await _snapshotProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            throw new InvalidOperationException("The database snapshot provider returned a null snapshot.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        InspectionRuleRegistration[] orderedRegistrations =
            [.. _registrations.OrderBy(registration => registration.Rule.Code.Value, StringComparer.Ordinal)];

        var executions = new List<DiagnosticExecution>(orderedRegistrations.Length);
        var acceptedFindings = new List<Finding>();
        var globallySeenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        void RecordUnhandledFailure(IInspectionRule failedRule)
        {
            executions.Add(DiagnosticExecution.Failed(
                failedRule.Code, failedRule.Version, failedRule.Name, failedRule.Category,
                new DiagnosticExecutionFailure(DiagnosticFailureKind.UnhandledRuleException, UnhandledRuleExceptionMessage)));
        }

        foreach (InspectionRuleRegistration registration in orderedRegistrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IInspectionRule rule = registration.Rule;

            List<CapabilityKind>? unavailableCapabilities = null;
            foreach (CapabilityKind requiredCapability in registration.RequiredCapabilities)
            {
                CapabilityState state = snapshot.Capabilities.GetState(requiredCapability);
                if (state.Status != CapabilityStatus.Available)
                {
                    (unavailableCapabilities ??= []).Add(requiredCapability);
                }
            }

            if (unavailableCapabilities is { Count: > 0 })
            {
                executions.Add(DiagnosticExecution.SkippedUnavailableCapability(
                    rule.Code, rule.Version, rule.Name, rule.Category, unavailableCapabilities));
                continue;
            }

            IReadOnlyList<Finding>? rawFindings;
            try
            {
                rawFindings = rule.Evaluate(snapshot);
            }
            catch (OperationCanceledException exception)
            {
                if (IsRequestedCancellation(exception, cancellationToken))
                {
                    // Genuinely associated with the requested cancellation: propagate
                    // immediately, without ever recording a diagnostic execution for this rule.
                    throw;
                }

                // Not associated with the requested token (for example CancellationToken.None,
                // or a different, unrelated token). This is a recoverable rule defect, not a
                // cancellation of the inspection — but if the requested token happened to become
                // canceled as a side effect of running this rule, cancellation still takes
                // priority over recording a failure.
                cancellationToken.ThrowIfCancellationRequested();
                RecordUnhandledFailure(rule);
                continue;
            }
            catch (Exception ex) when (IsRecoverableRuleException(ex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RecordUnhandledFailure(rule);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!TryValidateRuleOutput(rule, snapshot, rawFindings, globallySeenFingerprints, out IReadOnlyList<Finding> validatedFindings))
            {
                executions.Add(DiagnosticExecution.Failed(
                    rule.Code, rule.Version, rule.Name, rule.Category,
                    new DiagnosticExecutionFailure(
                        DiagnosticFailureKind.RuleContractViolation, RuleContractViolationMessage)));
                continue;
            }

            foreach (Finding finding in validatedFindings)
            {
                globallySeenFingerprints.Add(finding.Fingerprint.Value);
            }

            acceptedFindings.AddRange(validatedFindings);
            executions.Add(DiagnosticExecution.Completed(
                rule.Code, rule.Version, rule.Name, rule.Category, validatedFindings.Count));
        }

        DiagnosticExecution[] orderedExecutions =
            [.. executions.OrderBy(execution => execution.Code.Value, StringComparer.Ordinal)];
        Finding[] orderedFindings =
            [.. acceptedFindings
                .OrderBy(finding => finding.Code.Value, StringComparer.Ordinal)
                .ThenBy(finding => finding.Fingerprint.Value, StringComparer.Ordinal)];

        return new InspectionResult(snapshot, orderedExecutions, orderedFindings);
    }

    /// <summary>
    /// Validates one rule's raw <see cref="IInspectionRule.Evaluate"/> output against the full
    /// rule contract (see docs/design/inspection-orchestration.md) and, when valid, returns the
    /// findings ordered by fingerprint (ordinal). <paramref name="globallySeenFingerprints"/> is
    /// read but never mutated here; the caller commits accepted fingerprints only after this
    /// method returns <see langword="true"/>, so a rejected rule's findings never contaminate
    /// later duplicate checks.
    /// </summary>
    /// <remarks>
    /// Internal, and exercised directly in tests, because a genuine cross-rule fingerprint
    /// collision cannot be produced through the public API: <see cref="Finding.Fingerprint"/>
    /// always embeds <see cref="Finding.Code"/>, and this orchestrator's constructor already
    /// rejects two registrations sharing a code, so two different rules' findings can never
    /// legitimately collide. Testing the "already seen globally" branch therefore requires
    /// driving this method directly with a pre-populated set, exactly as
    /// <c>FindingFingerprintGenerator.EncodeCanonicalField</c> is exercised directly for a
    /// similarly unreachable-through-the-public-API scenario.
    /// </remarks>
    internal static bool TryValidateRuleOutput(
        IInspectionRule rule,
        DatabaseSnapshot snapshot,
        IReadOnlyList<Finding>? rawFindings,
        ISet<string> globallySeenFingerprints,
        out IReadOnlyList<Finding> validatedFindings)
    {
        validatedFindings = [];

        if (rawFindings is null)
        {
            return false;
        }

        foreach (Finding? finding in rawFindings)
        {
            if (finding is null
                || finding.Code != rule.Code
                || finding.RuleVersion != rule.Version
                || finding.Category != rule.Category
                || finding.Engine != snapshot.Metadata.Engine)
            {
                return false;
            }
        }

        var withinRuleFingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (Finding finding in rawFindings)
        {
            if (!withinRuleFingerprints.Add(finding.Fingerprint.Value))
            {
                return false;
            }
        }

        foreach (Finding finding in rawFindings)
        {
            if (globallySeenFingerprints.Contains(finding.Fingerprint.Value))
            {
                return false;
            }
        }

        validatedFindings = [.. rawFindings.OrderBy(finding => finding.Fingerprint.Value, StringComparer.Ordinal)];
        return true;
    }

    /// <summary>
    /// Excludes exceptions that indicate the process itself is compromised. A general recovery
    /// filter must never swallow these; everything else thrown by a rule is treated as an
    /// isolated, recoverable rule failure.
    /// </summary>
    private static bool IsRecoverableRuleException(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    /// <summary>
    /// Determines whether <paramref name="exception"/> represents the cancellation of
    /// <paramref name="requestedToken"/> specifically, as opposed to an unrelated
    /// <see cref="OperationCanceledException"/> a rule happened to throw (for example one
    /// carrying <see cref="CancellationToken.None"/> or some other token entirely). Two
    /// conditions each independently establish association:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>
    /// <paramref name="requestedToken"/> is itself already in the cancellation-requested state —
    /// in that case the exception's own token does not matter, because the inspection's own
    /// cancellation has unambiguously been requested.
    /// </description></item>
    /// <item><description>
    /// The exception's token is exactly <paramref name="requestedToken"/>, and both are
    /// cancelable. The <c>CanBeCanceled</c> checks on both sides exist specifically so that
    /// <see cref="CancellationToken.None"/> compared against another
    /// <see cref="CancellationToken.None"/> is never treated as association: two "no
    /// cancellation possible" tokens are structurally equal to each other but represent no
    /// relationship at all.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static bool IsRequestedCancellation(OperationCanceledException exception, CancellationToken requestedToken)
    {
        if (requestedToken.IsCancellationRequested)
        {
            return true;
        }

        return requestedToken.CanBeCanceled
            && exception.CancellationToken.CanBeCanceled
            && exception.CancellationToken == requestedToken;
    }
}
