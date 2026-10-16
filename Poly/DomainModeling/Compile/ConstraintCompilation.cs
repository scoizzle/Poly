using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compiles library-owned constraints into the lowered module. No compilers,
/// or none that match this domain, returns the same module instance.
/// </summary>
internal static class ConstraintCompilation {
    public static ContributedMethods.AppliedModule Apply(
        IReadOnlyList<TypeDefinitionNode> module,
        Domain domain,
        IReadOnlyList<IConstraintCompiler> compilers) {
        if (compilers.Count == 0)
            return new ContributedMethods.AppliedModule(module, []);

        var contributions = Compile(module, domain, compilers);
        if (contributions.Count == 0)
            return new ContributedMethods.AppliedModule(module, []);
        return ContributedMethods.Apply(module, contributions);
    }

    private static List<MethodContribution> Compile(
        IReadOnlyList<TypeDefinitionNode> module,
        Domain domain,
        IReadOnlyList<IConstraintCompiler> compilers) {
        var pending = new Dictionary<(string Library, string Type, string Method), Pending>(PendingKey.Comparer);
        foreach (var entity in domain.Types.OfType<Entity>().OrderBy(e => e.Name, StringComparer.Ordinal)) {
            var type = FindType(module, entity.Name);
            foreach (var property in entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
                foreach (var constraint in property.Constraints) {
                    IConstraintCompiler? match = null;
                    foreach (var compiler in compilers) {
                        if (!compiler.Applies(constraint))
                            continue;
                        if (match is not null)
                            throw new InvalidOperationException(
                                $"Constraint '{constraint.GetType().Name}' on {entity.Name}.{property.Name} is compiled by both '{match.LibraryId}' and '{compiler.LibraryId}'.");
                        match = compiler;
                    }
                    if (match is null)
                        continue;
                    var compiled = match.Compile(Request(entity, property, constraint, type));
                    Absorb(pending, match.LibraryId, entity.Name, compiled);
                }
            }
        }

        var contributions = new List<MethodContribution>(pending.Count);
        foreach (var item in pending.Values)
            contributions.Add(item.ToContribution());
        return contributions;
    }

    private static ConstraintCompileRequest Request(
        Entity entity, Property property, Constraint constraint, TypeDefinitionNode type) {
        var parameterName = DomainToCSharpExporter.ToCamelCase(property.Name);
        var parameters = new Dictionary<string, Parameter>(StringComparer.Ordinal);
        if (type.Methods is not null) {
            foreach (var method in type.Methods) {
                var parameter = method.Parameters?.FirstOrDefault(p =>
                    string.Equals(p.Name, parameterName, StringComparison.Ordinal));
                if (parameter is not null)
                    parameters[method.Name] = parameter;
            }
        }
        return new ConstraintCompileRequest(entity, property, constraint, parameters);
    }

    private static void Absorb(
        Dictionary<(string Library, string Type, string Method), Pending> pending,
        string libraryId,
        string typeName,
        CompiledConstraint compiled) {
        if (string.IsNullOrWhiteSpace(compiled.TargetMethod))
            throw new InvalidOperationException(
                $"Library '{libraryId}' compiled a constraint on '{typeName}' without a target method.");
        if (compiled.Statements.Count == 0
            && (compiled.SupportingMethods is null || compiled.SupportingMethods.Count == 0)
            && compiled.ImpliedParameters.Count == 0)
            throw new InvalidOperationException(
                $"Library '{libraryId}' compiled a constraint on '{typeName}' that produced no AST and no parameters.");
        ValidateParameters(libraryId, typeName, compiled.ImpliedParameters);

        if (compiled.SupportingMethods is not null) {
            foreach (var method in compiled.SupportingMethods) {
                if (string.IsNullOrWhiteSpace(method.Name))
                    throw new InvalidOperationException(
                        $"Library '{libraryId}' compiled a supporting method on '{typeName}' without a name.");
                var key = (libraryId, typeName, method.Name);
                if (pending.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"Library '{libraryId}' compiled {typeName}.{method.Name} more than once.");
                pending[key] = Pending.Method(libraryId, typeName, method, ParametersOn(method, compiled.ImpliedParameters));
            }
        }

        if (compiled.Statements.Count == 0 && compiled.ImpliedParameters.Count == 0)
            return;
        var target = (libraryId, typeName, compiled.TargetMethod);
        if (!pending.TryGetValue(target, out var existing)) {
            pending[target] = Pending.Injection(
                libraryId, typeName, compiled.TargetMethod, compiled.Statements, compiled.ImpliedParameters);
            return;
        }
        if (existing.NewMethod is not null)
            throw new InvalidOperationException(
                $"Library '{libraryId}' compiles {typeName}.{compiled.TargetMethod} as both a new method and an existing method.");
        existing.Add(compiled.Statements, compiled.ImpliedParameters);
    }

    private static void ValidateParameters(
        string libraryId, string typeName, IReadOnlyList<ImpliedParameter> parameters) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters) {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                throw new InvalidOperationException(
                    $"Library '{libraryId}' compiled an unnamed parameter on '{typeName}'.");
            if (parameter.Type is null)
                throw new InvalidOperationException(
                    $"Library '{libraryId}' compiled parameter '{parameter.Name}' on '{typeName}' without a type.");
            if (!names.Add(parameter.Name))
                throw new InvalidOperationException(
                    $"Library '{libraryId}' repeats parameter '{parameter.Name}' on '{typeName}'.");
        }
    }

    private static List<ContributedParameter> ParametersOn(
        MethodDefinitionNode method, IReadOnlyList<ImpliedParameter> implied) {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in method.Parameters ?? [])
            declared.Add(parameter.Name);
        var list = new List<ContributedParameter>();
        foreach (var parameter in implied) {
            if (!declared.Contains(parameter.Name))
                continue;
            list.Add(new ContributedParameter(parameter.Name, parameter.Type, parameter.ThreadToCallers));
        }
        return list;
    }

    private static TypeDefinitionNode FindType(IReadOnlyList<TypeDefinitionNode> module, string name) {
        TypeDefinitionNode? found = null;
        foreach (var type in module) {
            if (!string.Equals(type.Name, name, StringComparison.Ordinal))
                continue;
            if (found is not null)
                throw new InvalidOperationException(
                    $"Constraint compilation found two types named '{name}'.");
            found = type;
        }
        if (found is null)
            throw new InvalidOperationException(
                $"Entity '{name}' is not in the lowered module.");
        return found;
    }

    private sealed class Pending {
        private Pending(
            string libraryId,
            string typeName,
            string methodName,
            List<ContributedParameter> parameters,
            MethodDefinitionNode? newMethod,
            List<Node> statements,
            bool beforeConstruction) {
            LibraryId = libraryId;
            TypeName = typeName;
            MethodName = methodName;
            Parameters = parameters;
            NewMethod = newMethod;
            Statements = statements;
            BeforeConstruction = beforeConstruction;
        }

        public string LibraryId { get; }
        public string TypeName { get; }
        public string MethodName { get; }
        public List<ContributedParameter> Parameters { get; }
        public MethodDefinitionNode? NewMethod { get; }
        public List<Node> Statements { get; }
        public bool BeforeConstruction { get; }

        public static Pending Method(
            string libraryId, string typeName, MethodDefinitionNode method, List<ContributedParameter> parameters) =>
            new(libraryId, typeName, method.Name, parameters, method, [], false);

        public static Pending Injection(
            string libraryId,
            string typeName,
            string methodName,
            IReadOnlyList<Node> statements,
            IReadOnlyList<ImpliedParameter> parameters) {
            var pending = new Pending(libraryId, typeName, methodName, [], null, [], true);
            pending.Add(statements, parameters);
            return pending;
        }

        public void Add(IReadOnlyList<Node> statements, IReadOnlyList<ImpliedParameter> parameters) {
            Statements.AddRange(statements);
            foreach (var parameter in parameters) {
                var prior = Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, parameter.Name, StringComparison.Ordinal));
                if (prior is null) {
                    Parameters.Add(new ContributedParameter(parameter.Name, parameter.Type, parameter.ThreadToCallers));
                    continue;
                }
                if (!string.Equals(prior.Type.ToString(), parameter.Type.ToString(), StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Implied parameter '{parameter.Name}' on {TypeName}.{MethodName} is required as two types.");
                if (prior.ThreadToCallers != parameter.ThreadToCallers)
                    throw new InvalidOperationException(
                        $"Implied parameter '{parameter.Name}' on {TypeName}.{MethodName} disagrees about threading to callers.");
            }
        }

        public MethodContribution ToContribution() =>
            new(LibraryId, TypeName, MethodName, Parameters, NewMethod, Statements, BeforeConstruction);
    }

    private static class PendingKey {
        public static readonly IEqualityComparer<(string Library, string Type, string Method)> Comparer =
            new KeyComparer();

        private sealed class KeyComparer : IEqualityComparer<(string Library, string Type, string Method)> {
            public bool Equals(
                (string Library, string Type, string Method) x,
                (string Library, string Type, string Method) y) =>
                string.Equals(x.Library, y.Library, StringComparison.Ordinal)
                && string.Equals(x.Type, y.Type, StringComparison.Ordinal)
                && string.Equals(x.Method, y.Method, StringComparison.Ordinal);

            public int GetHashCode((string Library, string Type, string Method) obj) =>
                HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(obj.Library),
                    StringComparer.Ordinal.GetHashCode(obj.Type),
                    StringComparer.Ordinal.GetHashCode(obj.Method));
        }
    }
}