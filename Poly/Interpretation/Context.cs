using Poly.Extensions;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation;

public sealed class Context {
    private readonly TypeDefinitionProviderCollection _typeDefinitionProviderCollection;
    private readonly List<Parameter> _parameters = new();
    private readonly Stack<VariableScope> _scopes;
    private readonly VariableScope _globalScope;
    private VariableScope _currentScope;

    public Context() {
        _typeDefinitionProviderCollection = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
        _scopes = new Stack<VariableScope>();
        _currentScope = _globalScope = new VariableScope();
        _scopes.Push(_currentScope);
    }

    public IEnumerable<Parameter> Parameters => _parameters.AsReadOnly();

    public void AddTypeDefinitionProvider(ITypeDefinitionProvider provider) {
        _typeDefinitionProviderCollection.AddProvider(provider);
    }

    public ITypeDefinition? GetTypeDefinition(string name) => _typeDefinitionProviderCollection.GetTypeDefinition(name);
    public ITypeDefinition? GetTypeDefinition(Type type) => _typeDefinitionProviderCollection.GetTypeDefinition(type.SafeName());
    public ITypeDefinition? GetTypeDefinition<T>() => GetTypeDefinition(typeof(T));

    public Variable DeclareVariable(string name, Value? initialValue = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _currentScope.SetVariable(name, initialValue);
    }

    public Variable SetVariable(string name, Value value) => _currentScope.SetVariable(name, value);

    public Variable? GetVariable(string name) {
        foreach (var scope in _scopes) {
            var variable = scope.GetVariable(name);
            if (variable is not null) {
                return variable;
            }
        }
        return default;
    }
    
    public Parameter AddParameter(string name, Type type) {
        Parameter param = new Parameter(name, type);
        _parameters.Add(param);
        _globalScope.SetVariable(name, param);
        return param;
    }

    public Parameter AddParameter<T>(string name) => AddParameter(name, typeof(T));

    public void PushScope() {
        var newScope = new VariableScope();
        _scopes.Push(newScope);
        _currentScope = newScope;
    }

    public void PopScope() {
        if (_scopes.Count > 1) {
            _scopes.Pop();
            _currentScope = _scopes.Peek();
        } else {
            throw new InvalidOperationException("Cannot pop the global scope.");
        }
    }
}
