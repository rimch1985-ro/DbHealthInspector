using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// Pairs an <see cref="IInspectionRule"/> with the capabilities it requires to run, without
/// changing <see cref="IInspectionRule"/> itself.
/// </summary>
/// <remarks>
/// A collection of registrations supplied to <see cref="InspectionOrchestrator"/>'s constructor
/// represents exactly the rules enabled for that orchestrator instance. There is no separate
/// <c>Enabled</c> flag, global configuration, service locator, dependency-injection container or
/// reflection-based discovery: a future composition layer disables a rule simply by not
/// registering it.
/// </remarks>
public sealed class InspectionRuleRegistration
{
    /// <summary>
    /// The rule this registration enables.
    /// </summary>
    public IInspectionRule Rule { get; }

    /// <summary>
    /// The capabilities that must all be <see cref="CapabilityStatus.Available"/> in a snapshot
    /// before <see cref="InspectionOrchestrator"/> calls <see cref="Rule"/>'s
    /// <see cref="IInspectionRule.Evaluate"/>. May be empty when the rule needs only catalog data
    /// already guaranteed by <see cref="CapabilityKind.CatalogMetadata"/> being available. Order
    /// is preserved as supplied; the orchestrator's own execution order does not depend on it.
    /// </summary>
    public IReadOnlyList<CapabilityKind> RequiredCapabilities { get; }

    /// <summary>
    /// Creates a rule registration.
    /// </summary>
    /// <param name="rule">The rule to enable. Cannot be <see langword="null"/>.</param>
    /// <param name="requiredCapabilities">
    /// The capabilities required to run <paramref name="rule"/>. Copied defensively. Cannot be
    /// <see langword="null"/>, contain an undefined <see cref="CapabilityKind"/>, or contain a
    /// duplicate.
    /// </param>
    public InspectionRuleRegistration(IInspectionRule rule, IReadOnlyCollection<CapabilityKind> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Rule = rule;
        RequiredCapabilities = Guard.CopyDefensivelyRejectingUndefinedOrDuplicateEnumValues(
            requiredCapabilities, nameof(requiredCapabilities));
    }
}
