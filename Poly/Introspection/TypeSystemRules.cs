namespace Poly.Introspection;

/// <summary>
/// The rules registered with one type system, each under exactly the question it was registered for: an object that
/// implements two rule interfaces answers only the ones it was added as. A library registers a rule when the session
/// builds the system; a second rule for the same question is an error, never a silent replacement.
/// </summary>
public sealed class TypeSystemRules {
    private readonly Dictionary<object, ITypeSystemRule> _rules = [];

    /// <summary>Registers <paramref name="rule"/> as the system's <typeparamref name="TRule"/>.</summary>
    public void Add<TRule>(TRule rule) where TRule : class, ITypeSystemRule {
        ArgumentNullException.ThrowIfNull(rule);
        if (!_rules.TryAdd(Question<TRule>.Key, rule))
            throw new InvalidOperationException($"This type system already has a rule for the question '{rule.GetType().Name}' was registered as.");
    }

    /// <summary>The rule registered as <typeparamref name="TRule"/>, or null.</summary>
    public TRule? Find<TRule>() where TRule : class, ITypeSystemRule =>
        _rules.GetValueOrDefault(Question<TRule>.Key) as TRule;

    // One key object per question: the rule type itself, without naming a runtime type.
    private static class Question<TRule> where TRule : class, ITypeSystemRule {
        public static readonly object Key = new();
    }
}