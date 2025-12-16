namespace Poly.Introspection;

public interface ITypeDefinition {
    public string Name { get; }
    public string? Namespace { get; }
    public string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;
    public IEnumerable<ITypeMember> Members { get; }
    public Type ReflectedType { get; }

    public IEnumerable<ITypeMember> GetMembers(string name);
    public IEnumerable<ITypeMember> GetIndexers() => GetMembers("Item");

    /// <summary>
    /// Gets the base type of this type, or null if this is object or an interface.
    /// </summary>
    public ITypeDefinition? BaseType { get; }

    /// <summary>
    /// Gets all interfaces implemented by this type (including inherited).
    /// </summary>
    public IEnumerable<ITypeDefinition> Interfaces { get; }

    /// <summary>
    /// Determines if values of another type can be assigned to this type.
    /// </summary>
    public bool IsAssignableFrom(ITypeDefinition other) {
        if (this == other) return true;

        var current = other.BaseType;
        while (current != null) {
            if (this == current) return true;
            current = current.BaseType;
        }

        if (other.Interfaces.Any(i => this == i)) return true;

        return false;
    }

    /// <summary>
    /// Determines if this type can be assigned to another type.
    /// </summary>
    public bool IsAssignableTo(ITypeDefinition other) => other.IsAssignableFrom(this);
}