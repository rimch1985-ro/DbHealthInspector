namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// The reported state of every <see cref="CapabilityKind"/> for one inspection.
/// </summary>
/// <remarks>
/// Exactly one <see cref="CapabilityState"/> per defined <see cref="CapabilityKind"/> value is
/// required, so a capability can never silently disappear from the report: see ADR-0002.
/// </remarks>
public sealed class CapabilitySnapshot
{
    private readonly Dictionary<CapabilityKind, CapabilityState> _statesByKind;

    /// <summary>
    /// All reported capability states.
    /// </summary>
    public IReadOnlyCollection<CapabilityState> States { get; }

    /// <summary>
    /// Creates a capability snapshot.
    /// </summary>
    /// <param name="states">
    /// Must contain exactly one entry for every defined <see cref="CapabilityKind"/> value, with
    /// no duplicates and no unknown kind.
    /// </param>
    public CapabilitySnapshot(IReadOnlyCollection<CapabilityState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var statesByKind = new Dictionary<CapabilityKind, CapabilityState>();
        foreach (CapabilityState state in states)
        {
            if (state is null)
            {
                throw new ArgumentException("Collection cannot contain a null element.", nameof(states));
            }

            if (!statesByKind.TryAdd(state.Kind, state))
            {
                throw new ArgumentException(
                    $"Duplicate capability state for '{state.Kind}'.", nameof(states));
            }
        }

        foreach (CapabilityKind kind in Enum.GetValues<CapabilityKind>())
        {
            if (!statesByKind.ContainsKey(kind))
            {
                throw new ArgumentException(
                    $"Missing capability state for '{kind}'.", nameof(states));
            }
        }

        _statesByKind = statesByKind;
        States = Array.AsReadOnly(statesByKind.Values.ToArray());
    }

    /// <summary>
    /// Returns the reported state for <paramref name="kind"/>.
    /// </summary>
    public CapabilityState GetState(CapabilityKind kind) => _statesByKind[kind];
}
