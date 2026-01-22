namespace Poly.Interpretation;

public sealed class InterpretationScopeManager {
    private readonly Stack<VariableScope> _scopes;
    private readonly VariableScope _globalScope;
    private VariableScope _currentScope;

    public InterpretationScopeManager()
    {
        _currentScope = _globalScope = new();
        _scopes = new();
        _scopes.Push(_currentScope);
    }

    public VariableScope Current => _currentScope;

    public VariableScope Global => _globalScope;

    /// <summary>
    /// Gets or sets the maximum allowed scope depth to prevent stack overflow from excessive nesting.
    /// </summary>
    /// <value>The default is 256.</value>
    public int MaxScopeDepth { get; set; } = 256;

    /// <summary>
    /// Declares a new variable in the current scope.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="initialValue">The initial value, or <c>null</c> for an uninitialized variable.</param>
    /// <returns>The newly declared variable.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public Variable DeclareVariable(string name, Node? initialValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _currentScope.SetVariable(name, initialValue);
    }

    /// <summary>
    /// Sets the value of an existing variable or creates a new one in the current scope.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The variable that was set or created.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <remarks>
    /// If a variable with the given name exists in any scope, its value is updated.
    /// Otherwise, a new variable is created in the current scope.
    /// </remarks>
    public Variable SetVariable(string name, Node value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Variable? variable = GetVariable(name);
        if (variable is not null) {
            variable.Value = value;
            return variable;
        }
        return _currentScope.SetVariable(name, value);
    }

    /// <summary>
    /// Gets a variable by name, searching the current scope and all parent scopes.
    /// </summary>
    /// <param name="name">The name of the variable to retrieve.</param>
    /// <returns>The variable if found; otherwise, <c>null</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public Variable? GetVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _currentScope.GetVariable(name);
    }

    /// <summary>
    /// Pushes a new scope onto the scope stack, making it the current scope.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the maximum scope depth is exceeded.</exception>
    /// <remarks>
    /// Variables declared after this call will be in the new scope. Use <see cref="PopScope"/>
    /// to return to the previous scope.
    /// </remarks>
    public void PushScope()
    {
        if (_scopes.Count >= MaxScopeDepth)
            throw new InvalidOperationException("Maximum scope depth exceeded.");

        var newScope = new VariableScope(_currentScope);
        _scopes.Push(newScope);
        _currentScope = newScope;
    }

    /// <summary>
    /// Pops the current scope from the scope stack, restoring the previous scope.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when attempting to pop the global scope.</exception>
    /// <remarks>
    /// Variables declared in the popped scope will no longer be accessible.
    /// </remarks>
    public void PopScope()
    {
        if (_scopes.Count == 1)
            throw new InvalidOperationException("Cannot pop the global scope.");

        _scopes.Pop();
        _currentScope = _scopes.Peek();
    }

}