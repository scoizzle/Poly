using Poly.Extensions;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation;

public sealed class InterpretationContext
{
    private readonly TypeDefinitionProviderCollection _typeDefinitionProviderCollection;
    private readonly List<Parameter> _parameters = new();
    private readonly Stack<VariableScope> _scopes;
    private readonly VariableScope _globalScope;
    private VariableScope _currentScope;

    public InterpretationContext()
    {
        _typeDefinitionProviderCollection = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
        _scopes = new Stack<VariableScope>();
        _currentScope = _globalScope = new VariableScope();
        _scopes.Push(_currentScope);
    }

    public IEnumerable<Parameter> Parameters => _parameters.AsReadOnly();

    public void AddTypeDefinitionProvider(ITypeDefinitionProvider provider)
    {
        _typeDefinitionProviderCollection.AddProvider(provider);
    }

    public ITypeDefinition? GetTypeDefinition(string name) => _typeDefinitionProviderCollection.GetTypeDefinition(name);
    public ITypeDefinition? GetTypeDefinition(Type type) => _typeDefinitionProviderCollection.GetTypeDefinition(type.SafeName());
    public ITypeDefinition? GetTypeDefinition<T>() => _typeDefinitionProviderCollection.GetTypeDefinition(typeof(T).SafeName());

    public Variable DeclareVariable(string name, Value? initialValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _currentScope.SetVariable(name, initialValue);
    }

    public Variable SetVariable(string name, Value value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Variable? variable = GetVariable(name);
        if (variable is not null)
        {
            variable.Value = value;
            return variable;
        }
        return _currentScope.SetVariable(name, value);
    }

    public Variable? GetVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var scope in _scopes)
        {
            var variable = scope.GetVariable(name);
            if (variable is not null)
            {
                return variable;
            }
        }
        return default;
    }

    public Parameter AddParameter(string name, ITypeDefinition type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        Parameter param = new Parameter(name, type);
        _parameters.Add(param);
        _globalScope.SetVariable(name, param);
        return param;
    }

    public Parameter AddParameter<T>(string name) => AddParameter(name, GetTypeDefinition<T>()!);

    public void PushScope()
    {
        var newScope = new VariableScope(_currentScope);
        _scopes.Push(newScope);
        _currentScope = newScope;
    }

    public void PopScope()
    {
        if (_scopes.Count == 1)
            throw new InvalidOperationException("Cannot pop the global scope.");

        _scopes.Pop();
        _currentScope = _scopes.Peek();
    }
}
