using Poly.Extensions;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation;

/// <summary>
/// Provides context and state management for building interpretation trees and expression trees.
/// </summary>
/// <remarks>
/// <para>
/// The interpretation context manages type definitions, parameters, variables, and lexical scopes
/// during the construction of an interpretation tree. It serves as the central coordination point
/// for resolving types and managing the symbol table.
/// </para>
/// <para>
/// This class is NOT thread-safe. Each thread should use its own context instance or provide
/// external synchronization.
/// </para>
/// </remarks>
public sealed class InterpretationContext {
    private readonly TypeDefinitionProviderCollection _typeDefinitionProviderCollection;
    private readonly List<Parameter> _parameters = new();
    private readonly Stack<VariableScope> _scopes;
    private readonly VariableScope _globalScope;
    private VariableScope _currentScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterpretationContext"/> class.
    /// </summary>
    /// <remarks>
    /// The context is initialized with the CLR type definition registry and a global scope.
    /// </remarks>
    public InterpretationContext() {
        _typeDefinitionProviderCollection = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
        _scopes = new Stack<VariableScope>();
        _currentScope = _globalScope = new VariableScope();
        _scopes.Push(_currentScope);
    }

    /// <summary>
    /// Gets or sets the maximum allowed scope depth to prevent stack overflow from excessive nesting.
    /// </summary>
    /// <value>The default is 256.</value>
    public int MaxScopeDepth { get; set; } = 256;
    
    /// <summary>
    /// Gets a read-only collection of all parameters registered in this context.
    /// </summary>
    public IEnumerable<Parameter> Parameters => _parameters.AsReadOnly();

    /// <summary>
    /// Adds a custom type definition provider to this context.
    /// </summary>
    /// <param name="provider">The type definition provider to add.</param>
    public void AddTypeDefinitionProvider(ITypeDefinitionProvider provider) {
        _typeDefinitionProviderCollection.AddProvider(provider);
    }

    /// <summary>
    /// Gets the type definition for a given type name.
    /// </summary>
    /// <param name="name">The name of the type.</param>
    /// <returns>The type definition if found; otherwise, <c>null</c>.</returns>
    public ITypeDefinition? GetTypeDefinition(string name) => _typeDefinitionProviderCollection.GetTypeDefinition(name);
    
    /// <summary>
    /// Gets the type definition for a CLR type.
    /// </summary>
    /// <param name="type">The CLR type.</param>
    /// <returns>The type definition if found; otherwise, <c>null</c>.</returns>
    public ITypeDefinition? GetTypeDefinition(Type type) => _typeDefinitionProviderCollection.GetTypeDefinition(type);
    
    /// <summary>
    /// Gets the type definition for a generic type parameter.
    /// </summary>
    /// <typeparam name="T">The type to get the definition for.</typeparam>
    /// <returns>The type definition if found; otherwise, <c>null</c>.</returns>
    public ITypeDefinition? GetTypeDefinition<T>() => _typeDefinitionProviderCollection.GetTypeDefinition(typeof(T));

    /// <summary>
    /// Declares a new variable in the current scope.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="initialValue">The initial value, or <c>null</c> for an uninitialized variable.</param>
    /// <returns>The newly declared variable.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public Variable DeclareVariable(string name, Value? initialValue = null) {
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
    public Variable SetVariable(string name, Value value) {
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
    public Variable? GetVariable(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var scope in _scopes) {
            var variable = scope.GetVariable(name);
            if (variable is not null) {
                return variable;
            }
        }
        return default;
    }

    /// <summary>
    /// Adds a new parameter to the context.
    /// </summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="type">The type definition of the parameter.</param>
    /// <returns>The newly created parameter.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <c>null</c>.</exception>
    /// <remarks>
    /// The parameter is added to the global scope as a variable so it can be referenced by name.
    /// </remarks>
    public Parameter AddParameter(string name, ITypeDefinition type) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        Parameter param = new Parameter(name, type);
        _parameters.Add(param);
        _globalScope.SetVariable(name, param);
        return param;
    }

    /// <summary>
    /// Adds a new parameter with a generic type to the context.
    /// </summary>
    /// <typeparam name="T">The type of the parameter.</typeparam>
    /// <param name="name">The name of the parameter.</param>
    /// <returns>The newly created parameter.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the type <typeparamref name="T"/> is not registered in the context.</exception>
    public Parameter AddParameter<T>(string name) => AddParameter(name, GetTypeDefinition<T>()!);

    /// <summary>
    /// Pushes a new scope onto the scope stack, making it the current scope.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the maximum scope depth is exceeded.</exception>
    /// <remarks>
    /// Variables declared after this call will be in the new scope. Use <see cref="PopScope"/>
    /// to return to the previous scope.
    /// </remarks>
    public void PushScope() {
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
    public void PopScope() {
        if (_scopes.Count == 1)
            throw new InvalidOperationException("Cannot pop the global scope.");

        _scopes.Pop();
        _currentScope = _scopes.Peek();
    }
}