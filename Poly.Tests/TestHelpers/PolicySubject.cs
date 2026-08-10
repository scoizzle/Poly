namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Guards for <see cref="PolicyEvaluator"/> subjects. Validates that subject
/// types have real CLR properties (records, POCOs) and rejects raw dictionaries
/// or ExpandoObjects which cannot be used for policy evaluation.
///
/// For test subject factories and ad-hoc property bags, see
/// <c>Poly.Tests.TestHelpers.PolicyTestSubjects</c>.
/// </summary>
public static class PolicySubject {
    /// <summary>
    /// Throws <see cref="ArgumentException"/> if <paramref name="subject"/> is a
    /// raw Dictionary, ExpandoObject, or other unsupported bag type.
    /// Subjects must have real CLR properties (records, POCOs).
    /// </summary>
    public static void Validate(object? subject) {
        if (subject is null)
            throw new ArgumentNullException(nameof(subject), "Policy subject must not be null.");

        if (subject is System.Collections.IDictionary or IDictionary<string, object?>) {
            throw new ArgumentException(
                $"Policy subject type '{subject.GetType().Name}' is not supported. " +
                "Subjects must have real CLR properties (records, POCOs).");
        }
    }

    /// <summary>
    /// Validates <typeparamref name="T"/> at compile time — rejects forbidden
    /// subject types (Dictionary, ExpandoObject) without needing an instance.
    /// Throws <see cref="ArgumentException"/> if the type is forbidden.
    /// </summary>
    public static void ValidateType<T>() {
        var type = typeof(T);
        if (typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
            typeof(IDictionary<string, object?>).IsAssignableFrom(type) ||
            type == typeof(System.Dynamic.ExpandoObject)) {
            throw new ArgumentException(
                $"Subject type '{type.Name}' is not supported. " +
                "Subjects must have real CLR properties (records, POCOs).");
        }
    }

    /// <summary>
    /// Attempts to validate a subject. Returns <c>null</c> if valid, or an error
    /// message string if the subject type is unsupported.
    /// </summary>
    public static string? TryValidate(object? subject) {
        try {
            Validate(subject);
            return null;
        }
        catch (ArgumentException ex) {
            return ex.Message;
        }
    }
}