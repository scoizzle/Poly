using System.IO.Pipelines;

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
public sealed record InterpretationContext<TResult> : IInterpreterResultProvider<TResult> {
    private readonly TypeDefinitionProviderCollection _typeDefinitionProviderCollection;
    private readonly InterpretationMetadataStore _metadataStore;
    private readonly InterpretationScopeManager _scopeManager;
    private readonly TransformationDelegate<TResult> _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterpretationContext{TResult}"/> class.
    /// </summary>
    /// <remarks>
    /// The context is initialized with the CLR type definition registry and a global scope.
    /// </remarks>
    public InterpretationContext(TransformationDelegate<TResult> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _metadataStore = new InterpretationMetadataStore();
        _typeDefinitionProviderCollection = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
        _scopeManager = new InterpretationScopeManager();
        _pipeline = pipeline;
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
    /// Gets the metadata store for associating arbitrary data with AST nodes during interpretation.
    /// </summary>
    public InterpretationMetadataStore Metadata => _metadataStore;

    /// <summary>
    /// Gets the collection of type definition providers used for resolving types.
    /// </summary>
    public TypeDefinitionProviderCollection TypeDefinitionProviders => _typeDefinitionProviderCollection;

    /// <summary>
    /// Gets the scope manager for handling lexical scopes and symbol resolution.
    /// </summary>
    public InterpretationScopeManager Scopes => _scopeManager;

    /// <summary>
    /// Executes the interpretation pipeline on the given AST node.
    /// </summary>
    /// <param name="node">The AST node to interpret.</param>
    /// <returns>The interpreted result.</returns>
    public TResult Transform(Node node) => _pipeline(this, node);

    /// <summary>
    /// Applies the specified context initializer to this context.
    /// </summary>
    /// <param name="contextInitializer">The action to initialize the context.</param>
    /// <returns>The updated interpretation context.</returns>
    public InterpretationContext<TResult> With(Action<InterpretationContext<TResult>> contextInitializer)
    {
        ArgumentNullException.ThrowIfNull(contextInitializer);
        contextInitializer(this);
        return this;
    }

    /// <summary>
    /// Interprets an AST node by running it through the configured middleware pipeline.
    /// </summary>
    /// <param name="root">The AST node to interpret.</param>
    /// <returns>The interpretation result.</returns>
    public InterpretationResult<TResult> Interpret(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var result = _pipeline(this, root);
        return new InterpretationResult<TResult>(this, result);
    }
}
