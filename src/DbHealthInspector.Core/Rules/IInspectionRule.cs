using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// A pure diagnostic rule: it evaluates an engine-neutral <see cref="DatabaseSnapshot"/> and
/// returns findings, without performing any I/O of its own.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must perform no file, console, network or database access, must hold no
/// mutable state between calls, must not depend on a cancellation token for the pure evaluation
/// itself, and must return the same findings for the same snapshot every time. These properties
/// make every rule independently unit-testable without a database connection.
/// </para>
/// <para>
/// This gate defines the contract only. Concrete rules (DBH001 through DBH005) and the
/// orchestrator that runs them are implemented in a later gate.
/// </para>
/// </remarks>
public interface IInspectionRule
{
    /// <summary>
    /// The stable finding code this rule produces.
    /// </summary>
    FindingCode Code { get; }

    /// <summary>
    /// The version of this rule's implementation.
    /// </summary>
    RuleVersion Version { get; }

    /// <summary>
    /// A human-readable rule name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The technical category this rule's findings belong to.
    /// </summary>
    FindingCategory Category { get; }

    /// <summary>
    /// Evaluates <paramref name="snapshot"/> and returns the resulting findings. Must be pure
    /// and deterministic: the same snapshot always produces the same findings.
    /// </summary>
    IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot);
}
