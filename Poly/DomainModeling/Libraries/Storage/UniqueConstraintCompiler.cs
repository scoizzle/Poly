using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Runtime;

namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Compiles <c>unique</c> into a pre-construction guard over the in-process
/// storage port plus a post-construction pending-key record. The generated
/// context and the simulator both implement the port, so print and simulate
/// run the same guard. A domain with no storage mapping fails closed.
/// </summary>
public sealed class UniqueConstraintCompiler : IConstraintCompiler {
    public UniqueConstraintCompiler(string libraryId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        LibraryId = libraryId;
    }

    public string LibraryId { get; }

    public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

    public ConstraintContribution Compile(ConstraintCompileRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        var storage = request.RequireStorage();
        var portType = new ClrTypeReference(typeof(IStoragePort));
        var text = new ClrTypeReference(typeof(string));

        var entityName = storage.Name;
        var propertyName = request.Property.Name;
        var parameterName = DomainToCSharpExporter.ToCamelCase(propertyName);
        Parameter value = request.ParameterOn("Create") ?? new Parameter(parameterName, text);
        Node portValue = IsString(value) ? value : new Invoke(new Member(value, "ToString"));

        var methodName = "ExistsWith" + propertyName;
        var existsDb = new Parameter("db", portType);
        var existsValue = new Parameter(parameterName, text);
        var exists = new MethodDefinitionNode(
            methodName,
            new ClrTypeReference(typeof(bool)),
            Parameters: [existsDb, existsValue],
            Body: new Block([
                new Return(new Invoke(
                    new Member(existsDb, "Exists"),
                    new Constant(entityName),
                    new Constant(propertyName),
                    existsValue))
            ]),
            IsStatic: true);

        var db = new Parameter("db", portType);
        var failureType = new NamedTypeReference(
            "DomainResult", TypeArguments: [new NamedTypeReference(request.Entity.Name)]);
        Node guard = new IfStatement(
            new Invoke(new Member(new NamedTypeReference(request.Entity.Name), methodName), db, portValue),
            new Block([
                new Return(new Invoke(
                    new Member(failureType, "Failure"),
                    new Constant(
                        $"Unique constraint violated: '{propertyName}' value is already used on another '{request.Entity.Name}'.")))
            ]));
        Node record = new Invoke(
            new Member(db, "Record"),
            new Constant(entityName),
            new Constant(propertyName),
            portValue);

        return new ConstraintContribution(
            LibraryId,
            request.Entity.Name,
            new ConstraintAnchor.Create(),
            [guard],
            [record],
            [new ImpliedParameter("db", portType, ThreadToCallers: true)],
            [exists]);
    }

    private static bool IsString(Parameter value) => value.TypeReference switch {
        ClrTypeReference clr => clr.RuntimeType == typeof(string),
        PrimitiveTypeReference primitive => primitive.PrimitiveId.GetClrType() == typeof(string),
        NamedTypeReference named => string.Equals(named.TypeName, "Text", StringComparison.Ordinal),
        _ => false
    };
}