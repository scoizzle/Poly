using Poly.Introspection.CommonLanguageRuntime;
using Poly.Interpretation.SemanticAnalysis;

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
public sealed record InterpretationContext<TResult> {
    private readonly TypeDefinitionProviderCollection _typeDefinitionProviderCollection;
    private readonly Dictionary<Type, object> _metadata;
    private readonly List<Parameter> _parameters;
    private readonly Stack<VariableScope> _scopes;
    private readonly VariableScope _globalScope;
    private readonly TransformationDelegate<TResult> _pipeline;
    private VariableScope _currentScope;

    /// Initializes a new instance of the <see cref="InterpretationContext{TResult}"/> class.
    /// </summary>
    /// <remarks>
    /// The context is initialized with the CLR type definition registry and a global scope.
    /// </remarks>
    public InterpretationContext(TransformationDelegate<TResult> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
        _metadata = new();
        _typeDefinitionProviderCollection = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
        _parameters = new();
        _currentScope = _globalScope = new();
        _scopes = new();
        _scopes.Push(_currentScope);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InterpretationContext{TResult}"/> class with a custom type provider.
    /// </summary>
    public InterpretationContext(ITypeDefinitionProvider typeProvider, TransformationDelegate<TResult> pipeline) : this(pipeline)
    {
        ArgumentNullException.ThrowIfNull(typeProvider);
        _typeDefinitionProviderCollection.Add(typeProvider);
    }

    /// <summary>
    /// Executes the interpretation pipeline on the given AST node.
    /// </summary>
    public TResult Transform(Node node) => _pipeline(this, node);


    /// <summary>
    /// Gets or sets the maximum allowed scope depth to prevent stack overflow from excessive nesting.
    /// </summary>
    /// <value>The default is 256.</value>
    public int MaxScopeDepth { get; set; } = 256;

    /// <summary>
    /// Stores strongly-typed metadata contributed by middleware.
    /// Each middleware can define its own metadata type without coupling to others.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to store.</typeparam>
    /// <param name="data">The metadata instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public void SetMetadata<TMetadata>(TMetadata data) where TMetadata : class
    {
        ArgumentNullException.ThrowIfNull(data);
        _metadata[typeof(TMetadata)] = data;
    }

    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class
    {
        return _metadata.TryGetValue(typeof(TMetadata), out var data) ? (TMetadata)data : null;
    }


    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata GetOrAddMetadata<TMetadata>(Func<TMetadata> factory) where TMetadata : class
    {
        if (!_metadata.TryGetValue(typeof(TMetadata), out var data)) {
            data = factory();
            _metadata[typeof(TMetadata)] = data;
        }

        return (TMetadata)data;
    }

    /// <summary>
    /// Checks whether metadata of a given type has been set.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to check for.</typeparam>
    /// <returns>True if metadata of this type exists; otherwise, false.</returns>
    public bool HasMetadata<TMetadata>() where TMetadata : class
    {
        return _metadata.ContainsKey(typeof(TMetadata));
    }

    /// <summary>
    /// Removes metadata of a given type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to remove.</typeparam>
    public void RemoveMetadata<TMetadata>() where TMetadata : class
    {
        _metadata.Remove(typeof(TMetadata));
    }

    /// <summary>
    /// Adds a custom type definition provider to this context.
    /// </summary>
    /// <param name="provider">The type definition provider to add.</param>
    public void AddTypeDefinitionProvider(ITypeDefinitionProvider provider)
    {
        _typeDefinitionProviderCollection.Add(provider);
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
    /// Adds a new parameter to the context.
    /// </summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="type">The type definition of the parameter.</param>
    /// <returns>The newly created parameter.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Creates a Parameter node (AST) and stores its type information through the semantic analysis system.
    /// The Parameter node itself remains pure syntax; type resolution flows through context.SetResolvedType.
    /// </remarks>
    public Parameter AddParameter(string name, ITypeDefinition type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        Parameter param = new Parameter(name);
        _parameters.Add(param);
        this.SetResolvedType(param, type);
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