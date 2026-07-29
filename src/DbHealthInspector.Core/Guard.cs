namespace DbHealthInspector.Core;

/// <summary>
/// Shared, dependency-free argument validation helpers used across the Core domain model.
/// </summary>
internal static class Guard
{
    /// <summary>
    /// Throws when <paramref name="value"/> is <see langword="null"/>, empty or made only of
    /// whitespace. Returns the original, unmodified value otherwise.
    /// </summary>
    public static string AgainstNullOrWhiteSpace(string? value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    /// <summary>
    /// Validates an optional string: <see langword="null"/> is allowed and returned as-is, but
    /// an empty or whitespace-only value throws. The value is never trimmed or otherwise
    /// altered; empty is deliberately not normalized to <see langword="null"/> so callers that
    /// need to distinguish "absent" from "blank" at a lower level still can.
    /// </summary>
    public static string? AgainstEmptyOrWhiteSpace(string? value, string paramName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be empty or consist only of whitespace when provided.", paramName);
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is not one of the named members of <typeparamref name="TEnum"/>.
    /// </summary>
    public static void AgainstUndefinedEnum<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Undefined {typeof(TEnum).Name} value.");
        }
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is negative. Returns the original value otherwise.
    /// </summary>
    public static long AgainstNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is negative. Returns the original value otherwise.
    /// </summary>
    public static int AgainstNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        return value;
    }

    /// <summary>
    /// Throws when <paramref name="source"/> is <see langword="null"/>. Otherwise returns a
    /// defensive, independent, genuinely non-modifiable copy: the result is wrapped with
    /// <see cref="Array.AsReadOnly{T}(T[])"/>, so unlike a plain array, casting the result to
    /// <see cref="IList{T}"/> and calling <c>Add</c>, index-assigning (<c>list[0] = x</c>) or
    /// <c>Remove</c>/<c>RemoveAt</c> all throw <see cref="NotSupportedException"/>. The backing
    /// array is never referenced by the caller, so later mutation of <paramref name="source"/>
    /// cannot affect the returned collection either.
    /// </summary>
    public static IReadOnlyList<T> CopyDefensively<T>(IReadOnlyCollection<T>? source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        return Array.AsReadOnly(source.ToArray());
    }

    /// <summary>
    /// Throws when <paramref name="source"/> is <see langword="null"/> or contains a
    /// <see langword="null"/> element. Otherwise returns a defensive, independent,
    /// order-preserving, genuinely non-modifiable copy — see <see cref="CopyDefensively"/> for
    /// exactly which mutation attempts throw <see cref="NotSupportedException"/>.
    /// </summary>
    public static IReadOnlyList<T> CopyDefensivelyRejectingNullElements<T>(
        IReadOnlyCollection<T>? source, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        var copy = new T[source.Count];
        int index = 0;
        foreach (T item in source)
        {
            if (item is null)
            {
                throw new ArgumentException("Collection cannot contain a null element.", paramName);
            }

            copy[index] = item;
            index++;
        }

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Throws when <paramref name="source"/> is <see langword="null"/>, or contains a
    /// <see langword="null"/>, empty or whitespace-only element. Otherwise returns a defensive,
    /// independent, order-preserving, genuinely non-modifiable copy — see
    /// <see cref="CopyDefensively"/> for exactly which mutation attempts throw
    /// <see cref="NotSupportedException"/>. Elements are validated and copied as-is, never trimmed.
    /// </summary>
    public static IReadOnlyList<string> CopyDefensivelyRejectingBlankElements(
        IReadOnlyCollection<string>? source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        var copy = new string[source.Count];
        int index = 0;
        foreach (string item in source)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                throw new ArgumentException(
                    "Collection cannot contain a null, empty or whitespace-only element.", paramName);
            }

            copy[index] = item;
            index++;
        }

        return Array.AsReadOnly(copy);
    }
}
