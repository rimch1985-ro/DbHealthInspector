namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// A small, stable classification of why a rule's execution failed.
/// </summary>
public enum DiagnosticFailureKind
{
    /// <summary>
    /// The rule's <see cref="Rules.IInspectionRule.Evaluate"/> threw an exception.
    /// </summary>
    UnhandledRuleException,

    /// <summary>
    /// The rule returned a result that violates the rule contract (for example a null
    /// collection, a null finding, a mismatched code, or a duplicate fingerprint).
    /// </summary>
    RuleContractViolation,
}
