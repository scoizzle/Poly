namespace Poly.Introspection;

public interface ITypeAdapter
{
    public string Name { get; }
    public string GloballyUniqueName { get; }
    public IEnumerable<ITypeMemberAdapter> Members { get; }
    public IEnumerable<IMethodAdapter> Constructors { get; }
    public IEnumerable<IMethodAdapter> Methods { get; }
}

sealed record MyTypeInfo(
    string Name,
    string GloballyUniqueName,
    IEnumerable<ITypeMemberAdapter> Members,
    IEnumerable<IMethodAdapter> Constructors,
    IEnumerable<IMethodAdapter> Methods
);

sealed record MyTypeMemberInfo(
    string Name,
    MyTypeInfo Type,
    bool IsStatic = false,
    bool IsReadOnly = false
);

sealed record MyMethodParameterInfo(
    string Name,
    MyTypeInfo Type,
    bool IsOptional = false,
    object? DefaultValue = null
);

sealed record MyMethodInfo(
    string Name,
    IEnumerable<IMethodParameterAdapter> Parameters,
    MyTypeInfo ReturnType
);