using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

public sealed partial class DomainToCSharpExporter {
    private const int StoreJobPairSlots = 8;

    /// <summary>
    /// Host bind of Store jobs the operation tree names: <c>Create</c> /
    /// <c>CreateIn</c> / <c>ProbeCreate</c> / <c>EnsureUnique</c>. Bodies wrap
    /// existing <c>Stay.Create</c> / <c>CreateNav</c> factories. Unique is a
    /// Success stub — persistence indexes remain the schema concern.
    /// </summary>
    private static void AddStoreBindMethods(
        Entity entity,
        Domain domain,
        INodeMetadataProvider metadata,
        List<MethodDefinitionNode> methods) {
        methods.Add(new MethodDefinitionNode(
            "EnsureUnique",
            new NamedTypeReference("DomainResult"),
            Parameters: [
                new Parameter("propertyName", new PrimitiveTypeReference(PrimType.String)),
                new Parameter("value", new NamedTypeReference("object"))
            ],
            Body: new Block([
                new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")))
            ]),
            AccessModifier: AccessModifier.Public));

        var dictType = DictionaryType();
        var objectResult = new NamedTypeReference("DomainResult",
            TypeArguments: [new NamedTypeReference("object")]);
        var voidResult = new NamedTypeReference("DomainResult");
        AddStoreJobOverloads(methods, "Create", objectResult, "BindCreate", dictType);
        AddStoreJobOverloads(methods, "CreateIn", objectResult, "BindCreateIn", dictType);
        AddStoreJobOverloads(methods, "ProbeCreate", voidResult, "BindProbeCreate", dictType);

        methods.Add(BindCreateMethod(entity, domain, metadata, objectResult, dictType));
        methods.Add(BindCreateInMethod(entity, domain, metadata, objectResult, dictType));
        methods.Add(BindProbeCreateMethod(entity, domain, metadata, voidResult, dictType));
    }

    private static NamedTypeReference DictionaryType() =>
        new("Dictionary", TypeArguments: [
            new PrimitiveTypeReference(PrimType.String),
            new OptionalTypeReference(new NamedTypeReference("object"))
        ]);

    private static void AddStoreJobOverloads(
        List<MethodDefinitionNode> methods,
        string methodName,
        Node returnType,
        string bindName,
        Node dictType) {
        for (var pairs = 0; pairs <= StoreJobPairSlots; pairs++) {
            var parameters = new List<Parameter> {
                new("name", new PrimitiveTypeReference(PrimType.String))
            };
            var body = new List<Node>();
            var locals = new List<Node>();
            var values = new Variable("values");
            locals.Add(values);
            body.Add(new Assignment(values, new New(dictType)));
            for (var i = 0; i < pairs; i++) {
                var p = $"p{i}";
                var v = $"v{i}";
                parameters.Add(new Parameter(p, new PrimitiveTypeReference(PrimType.String)));
                parameters.Add(new Parameter(v, new NamedTypeReference("object")));
                body.Add(new Invoke(new Member(values, "Add"),
                    new Parameter(p), new Parameter(v)));
            }
            body.Add(new Return(new Invoke(new Member(new ThisReference(), bindName),
                new Parameter("name"), values)));
            methods.Add(new MethodDefinitionNode(
                methodName,
                returnType,
                Parameters: parameters,
                Body: new Block(body, locals),
                AccessModifier: AccessModifier.Public));
        }
    }

    private static MethodDefinitionNode BindCreateMethod(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        Node objectResult, Node dictType) {
        var name = new Parameter("typeName", new PrimitiveTypeReference(PrimType.String));
        var values = new Parameter("values", dictType);
        var cases = new List<Node>();
        foreach (var target in domain.Types.OfType<Entity>()) {
            var typed = new Variable("typed");
            var createArgs = BuildTargetCreateArgs(entity, target, values, domain, metadata);
            cases.Add(new IfStatement(
                new Equal(name, new Constant(target.Name)),
                new Block([
                    new Assignment(typed, new Invoke(
                        new Member(new NamedTypeReference(target.Name), "Create"),
                        [.. createArgs])),
                    RewrapObjectResult(typed, objectResult)
                ], [typed])));
        }
        cases.Add(UnknownFailure(objectResult, "Unknown type '", name, "'."));
        return new MethodDefinitionNode(
            "BindCreate",
            objectResult,
            Parameters: [name, values],
            Body: new Block(cases),
            AccessModifier: AccessModifier.Private);
    }

    private static MethodDefinitionNode BindProbeCreateMethod(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        Node voidResult, Node dictType) {
        var name = new Parameter("typeName", new PrimitiveTypeReference(PrimType.String));
        var values = new Parameter("values", dictType);
        var cases = new List<Node>();
        foreach (var target in domain.Types.OfType<Entity>()) {
            var typed = new Variable("typed");
            var createArgs = BuildTargetCreateArgs(entity, target, values, domain, metadata);
            cases.Add(new IfStatement(
                new Equal(name, new Constant(target.Name)),
                new Block([
                    new Assignment(typed, new Invoke(
                        new Member(new NamedTypeReference(target.Name), "Create"),
                        [.. createArgs])),
                    new IfStatement(
                        new Syntactic.Not(new Member(typed, "IsSuccess")),
                        new Block([
                            new Return(new Invoke(
                                new Member(voidResult, "Failure"),
                                new Syntactic.Coalesce(
                                    new Member(typed, "ErrorMessage"),
                                    new Constant(""))))
                        ])),
                    new Return(new Invoke(new Member(voidResult, "Success")))
                ], [typed])));
        }
        cases.Add(UnknownFailure(voidResult, "Unknown type '", name, "'."));
        return new MethodDefinitionNode(
            "BindProbeCreate",
            voidResult,
            Parameters: [name, values],
            Body: new Block(cases),
            AccessModifier: AccessModifier.Private);
    }

    private static MethodDefinitionNode BindCreateInMethod(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        Node objectResult, Node dictType) {
        var name = new Parameter("relationshipName", new PrimitiveTypeReference(PrimType.String));
        var values = new Parameter("values", dictType);
        var cases = new List<Node>();
        foreach (var rel in entity.Navigations) {
            var lookup = metadata.GetTypeLookup(domain);
            if (lookup is null
                || !lookup.Types.TryGetValue(rel.Target.TypeName, out var resolved)
                || resolved is not Entity target)
                continue;
            var typed = new Variable("typed");
            var navArgs = BuildCreateNavArgs(entity, target, values, domain, metadata);
            var methodName = $"Create{ToPascalCase(rel.Name)}";
            cases.Add(new IfStatement(
                new Equal(name, new Constant(rel.Name)),
                new Block([
                    new Assignment(typed, new Invoke(
                        new Member(new ThisReference(), methodName),
                        [.. navArgs])),
                    RewrapObjectResult(typed, objectResult)
                ], [typed])));
        }
        cases.Add(UnknownFailure(objectResult, "Unknown relationship '", name, "'."));
        return new MethodDefinitionNode(
            "BindCreateIn",
            objectResult,
            Parameters: [name, values],
            Body: new Block(cases),
            AccessModifier: AccessModifier.Private);
    }

    private static Node RewrapObjectResult(Variable typed, Node objectResult) =>
        new IfStatement(
            new Syntactic.Not(new Member(typed, "IsSuccess")),
            new Block([
                new Return(new Invoke(
                    new Member(objectResult, "Failure"),
                    new Syntactic.Coalesce(
                        new Member(typed, "ErrorMessage"),
                        new Constant(""))))
            ]),
            new Block([
                new Return(new Invoke(
                    new Member(objectResult, "Success"),
                    new Member(typed, "Value")))
            ]));

    private static Node UnknownFailure(Node resultType, string prefix, Parameter name, string suffix) =>
        new Return(new Invoke(
            new Member(resultType, "Failure"),
            new Syntactic.Add(
                new Syntactic.Add(new Constant(prefix), name),
                new Constant(suffix))));

    private static List<Node> BuildTargetCreateArgs(
        Entity source,
        Entity target,
        Parameter values,
        Domain domain,
        INodeMetadataProvider metadata) {
        var args = new List<Node>();
        var parameters = GetConstructorParameters(target, metadata);
        foreach (var parameter in parameters) {
            if (parameter.IsBackReference) {
                args.Add(string.Equals(source.Name, target.Name, StringComparison.Ordinal)
                    ? new ThisReference()
                    : new Constant(null));
                continue;
            }
            if (parameter.IsCollection) {
                args.Add(new New(new NamedTypeReference("List",
                    TypeArguments: [new NamedTypeReference(parameter.Type.TypeName)])));
                continue;
            }
            args.Add(ValueFromDictionary(values, parameter.Name, parameter.Type, domain, metadata));
        }
        AppendDefaultedFromDictionary(args, values, target, domain, metadata);
        return args;
    }

    private static List<Node> BuildCreateNavArgs(
        Entity source,
        Entity target,
        Parameter values,
        Domain domain,
        INodeMetadataProvider metadata) {
        var args = new List<Node>();
        var parameters = GetConstructorParameters(target, metadata);
        var autoWire = FindAutoWireBackReference(target, source.Name);
        foreach (var parameter in parameters) {
            if (parameter.IsBackReference) continue;
            if (parameter.IsCollection) continue;
            if (autoWire is not null
                && string.Equals(parameter.Name, autoWire.Name, StringComparison.Ordinal))
                continue;
            args.Add(ValueFromDictionary(values, parameter.Name, parameter.Type, domain, metadata));
        }
        AppendDefaultedFromDictionary(args, values, target, domain, metadata);
        return args;
    }

    private static void AppendDefaultedFromDictionary(
        List<Node> args,
        Parameter values,
        Entity target,
        Domain domain,
        INodeMetadataProvider metadata) {
        var entryAssigned = metadata.GetStructure(target)?.EntryAssignedPropertyNames
            ?? EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(target);
        foreach (var prop in target.Properties.OrderBy(p => p.Name)) {
            if (!prop.Constraints.Any(c => c is DefaultValueConstraint)) continue;
            if (entryAssigned.Contains(prop.Name)) continue;
            args.Add(ValueFromDictionary(values, prop.Name, prop.Type, domain, metadata));
        }
    }

    private static Node ValueFromDictionary(
        Parameter values,
        string propertyName,
        DomainTypeReference type,
        Domain domain,
        INodeMetadataProvider metadata) {
        var mapped = MapDomainTypeRef(type, domain, metadata);
        if (domain.Types.OfType<Entity>().Any(e =>
            string.Equals(e.Name, type.TypeName, StringComparison.Ordinal))) {
            mapped = new OptionalTypeReference(mapped);
        }
        var fetched = new Invoke(
            new Member(values, "GetValueOrDefault"),
            new Constant(propertyName));
        var fallback = DefaultNode(type, domain, metadata);
        return new TypeCast(new Syntactic.Coalesce(fetched, fallback), mapped);
    }

    private static Node DefaultNode(
        DomainTypeReference type, Domain domain, INodeMetadataProvider metadata) {
        if (TryResolveEnumType(domain, metadata, type.TypeName, out var enumType) && enumType is not null)
            return new Member(new NamedTypeReference(enumType.Name), enumType.MemberNames[0]);
        return type.TypeName switch {
            "Text" or "String" => new Constant(""),
            "Number" or "Int" or "Int64" => new Constant(0L),
            "Int32" => new Constant(0),
            "Boolean" or "Bool" => new Constant(false),
            "DateTime" or "Timestamp" => new Member(new NamedTypeReference("DateTime"), "MinValue"),
            "Date" or "DateOnly" => new Member(new NamedTypeReference("DateOnly"), "MinValue"),
            "Guid" or "Uuid" => new Member(new NamedTypeReference("Guid"), "Empty"),
            _ => new Constant(null)
        };
    }
}
