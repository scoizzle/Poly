using Poly.Extensions;

namespace Poly.Interpretation;

/// <summary>
/// Represents a lexical scope that contains named variables.
/// </summary>
/// <remarks>
/// Variable scopes form a chain through parent references, enabling lexical scoping
/// with shadowing. Variables are looked up first in the current scope, then in parent
/// scopes recursively. This class is NOT thread-safe and should only be used from a
/// single thread or with external synchronization.
/// </remarks>
public sealed class VariableScope<T>(VariableScope<T>? parentScope = null) {
    /// <summary>
    /// Gets the parent scope, if any.
    /// </summary>
    /// <remarks>
    /// The parent scope is searched when a variable is not found in the current scope,
    /// implementing lexical scoping rules.
    /// </remarks>
    public VariableScope<T>? ParentScope { get; } = parentScope;

    /// <summary>
    /// Gets the dictionary of variables defined in this scope.
    /// </summary>
    public Dictionary<string, VariableReference<T>> Variables { get; private init; } = new();

    /// <summary>
    /// Retrieves a variable by name, searching this scope and parent scopes.
    /// </summary>
    /// <param name="name">The name of the variable to retrieve.</param>
    /// <returns>The variable if found; otherwise, <c>null</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public VariableReference<T>? GetVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Variables.TryGetValue(name, out var variable)
            ? variable
            : ParentScope?.GetVariable(name);
    }

    /// <summary>
    /// Sets a variable in this scope, creating it if it doesn't exist.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="value">The initial value for the variable, or <c>null</c> for uninitialized.</param>
    /// <returns>The variable that was set or created.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <remarks>
    /// This method will create a new variable in this scope even if a variable with the same
    /// name exists in a parent scope, implementing variable shadowing.
    /// </remarks>
    public VariableReference<T> SetVariable(string name, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Variables.GetOrAdd(name, static (name, value) => new VariableReference<T>(name, value), value);
    }

    /// <summary>
    /// Creates a shallow copy of this scope with the same parent.
    /// </summary>
    /// <returns>A new scope with copied variables but the same parent reference.</returns>
    public VariableScope<T> Clone()
    {
        var clone = new VariableScope<T>(ParentScope) {
            Variables = new Dictionary<string, VariableReference<T>>(Variables)
        };
        return clone;
    }
}