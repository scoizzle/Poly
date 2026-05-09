using System.Reflection;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Recipes.Contracts.Clr;

public sealed class ClrContractImportRecipe : IContractImportRecipe {
    public string Name => "ClrContractImport";

    public bool CanImport(ContractImportSource source) =>
        source is ContractImportSource.ClrType or ContractImportSource.ClrAssembly;

    public ContractImportResult ImportInto(Domain domain, ContractImportSource source, ContractImportOptions? options = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(source);

        if (!CanImport(source)) {
            throw new InvalidOperationException($"Source '{source.GetType().Name}' is not supported by recipe '{Name}'.");
        }

        options ??= new ContractImportOptions();
        var mutation = domain.CreateMutation();
        var resolver = new ContractImportTypeResolver(domain, mutation, options.TypeNameTransform);
        var sourceVersion = source switch {
            ContractImportSource.ClrType clrType => clrType.Version,
            ContractImportSource.ClrAssembly clrAssembly => clrAssembly.Version,
            _ => "v1"
        };

        var contractName = options.ContractName ?? ResolveDefaultContractName(source);
        if (domain.FindImportedContract(contractName) is not null) {
            throw new InvalidOperationException($"Imported contract '{contractName}' already exists in domain '{domain.Name}'.");
        }

        var contract = new ImportedContract(domain, contractName, options.SourceKind, ResolveSourceIdentifier(source), sourceVersion);
        mutation.AddImportedContract(contract);

        var endpoints = new List<ContractEndpoint>();
        foreach (var type in ResolveTypes(source)) {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                if (method.IsSpecialName || method.ReturnType == typeof(void) && method.GetParameters().Length == 0 && !options.IncludeMethodsWithoutPayload) {
                    continue;
                }

                var payloadType = ResolveMethodPayloadType(method, resolver);
                if (payloadType is null) {
                    continue;
                }

                var endpointName = TransformEndpointName($"{type.Name}_{method.Name}", options);
                var endpoint = new ContractEndpoint(
                    domain,
                    endpointName,
                    ContractEndpointKind.Operation,
                    options.DefaultDirection,
                    payloadType);
                mutation.AddContractEndpoint(contract, endpoint);
                endpoints.Add(endpoint);
            }

            foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                var handlerType = eventInfo.EventHandlerType;
                if (handlerType is null) {
                    continue;
                }

                var invokeMethod = handlerType.GetMethod("Invoke");
                if (invokeMethod is null) {
                    continue;
                }

                var eventArgsParameter = invokeMethod.GetParameters().Skip(1).FirstOrDefault();
                var payloadType = resolver.ResolveClrType(eventArgsParameter?.ParameterType ?? typeof(string));
                var endpointName = TransformEndpointName($"{type.Name}_{eventInfo.Name}", options);
                var endpoint = new ContractEndpoint(
                    domain,
                    endpointName,
                    ContractEndpointKind.Event,
                    options.DefaultDirection,
                    payloadType);
                mutation.AddContractEndpoint(contract, endpoint);
                endpoints.Add(endpoint);
            }
        }

        var analysis = mutation.Apply();
        return new ContractImportResult(contract, endpoints, resolver.CreatedTypes, analysis);
    }

    private static DomainType? ResolveMethodPayloadType(MethodInfo method, ContractImportTypeResolver resolver) {
        var parameters = method.GetParameters()
            .Where(static parameter => !parameter.IsOut && !parameter.ParameterType.IsByRef)
            .ToArray();

        if (parameters.Length > 0) {
            if (parameters.Length == 1) {
                return resolver.ResolveClrType(parameters[0].ParameterType);
            }

            return resolver.ResolveClrType(typeof(string));
        }

        if (method.ReturnType == typeof(void)) {
            return null;
        }

        return resolver.ResolveClrType(method.ReturnType);
    }

    private static IEnumerable<Type> ResolveTypes(ContractImportSource source) {
        return source switch {
            ContractImportSource.ClrType typeSource => [typeSource.RootType],
            ContractImportSource.ClrAssembly assemblySource => assemblySource.Assembly
                .GetExportedTypes()
                .Where(type => !type.IsNested && (assemblySource.TypeFilter?.Invoke(type) ?? true)),
            _ => []
        };
    }

    private static string ResolveDefaultContractName(ContractImportSource source) {
        return source switch {
            ContractImportSource.ClrType clrType => $"{clrType.RootType.Name}Contract",
            ContractImportSource.ClrAssembly clrAssembly => $"{clrAssembly.Assembly.GetName().Name ?? "Assembly"}Contract",
            _ => "ClrContract"
        };
    }

    private static string ResolveSourceIdentifier(ContractImportSource source) {
        return source switch {
            ContractImportSource.ClrType clrType => clrType.RootType.AssemblyQualifiedName ?? clrType.RootType.FullName ?? clrType.RootType.Name,
            ContractImportSource.ClrAssembly clrAssembly => clrAssembly.Assembly.FullName ?? clrAssembly.Assembly.GetName().Name ?? "ClrAssembly",
            _ => "ClrSource"
        };
    }

    private static string TransformEndpointName(string value, ContractImportOptions options) {
        return options.EndpointNameTransform?.Invoke(value) ?? value;
    }
}