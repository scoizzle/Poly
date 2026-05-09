using System.Text.Json;

namespace Poly.Data.Modeling.Recipes.Contracts.OpenApi;

public sealed class OpenApiContractImportRecipe : IContractImportRecipe {
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase) {
        "get", "put", "post", "delete", "options", "head", "patch", "trace"
    };

    public string Name => "OpenApiContractImport";

    public bool CanImport(ContractImportSource source) =>
        source is ContractImportSource.OpenApiDocument or ContractImportSource.OpenApiJson;

    public ContractImportResult ImportInto(Domain domain, ContractImportSource source, ContractImportOptions? options = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(source);

        if (!CanImport(source)) {
            throw new InvalidOperationException($"Source '{source.GetType().Name}' is not supported by recipe '{Name}'.");
        }

        options ??= new ContractImportOptions();
        using var ownedDocument = source is ContractImportSource.OpenApiJson openApiJsonSource
            ? JsonDocument.Parse(openApiJsonSource.Json)
            : null;
        var document = source switch {
            ContractImportSource.OpenApiDocument openApiDocument => openApiDocument.Document,
            ContractImportSource.OpenApiJson => ownedDocument ?? throw new InvalidOperationException("Invalid OpenAPI JSON document."),
            _ => throw new InvalidOperationException("Unsupported OpenAPI source.")
        };

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("OpenAPI source must contain a top-level 'paths' object.");
        }

        var mutation = domain.CreateMutation();
        var resolver = new ContractImportTypeResolver(domain, mutation, options.TypeNameTransform);
        var version = source switch {
            ContractImportSource.OpenApiDocument openApiDocument => openApiDocument.Version,
            ContractImportSource.OpenApiJson openApiJson => openApiJson.Version,
            _ => "v1"
        };
        var contractName = options.ContractName ?? ResolveContractName(root);

        if (domain.FindImportedContract(contractName) is not null) {
            throw new InvalidOperationException($"Imported contract '{contractName}' already exists in domain '{domain.Name}'.");
        }

        var sourceIdentifier = root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0
            ? servers[0].TryGetProperty("url", out var serverUrl) && serverUrl.ValueKind == JsonValueKind.String
                ? serverUrl.GetString() ?? contractName
                : contractName
            : contractName;

        var contract = new ImportedContract(domain, contractName, options.SourceKind, sourceIdentifier, version);
        mutation.AddImportedContract(contract);

        var endpoints = new List<ContractEndpoint>();
        foreach (var path in paths.EnumerateObject()) {
            if (path.Value.ValueKind != JsonValueKind.Object) {
                continue;
            }

            foreach (var operation in path.Value.EnumerateObject()) {
                if (!HttpMethods.Contains(operation.Name)) {
                    continue;
                }

                if (operation.Value.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                var endpointName = ResolveEndpointName(path.Name, operation.Name, operation.Value, options);
                if (string.IsNullOrWhiteSpace(endpointName)) {
                    continue;
                }

                var payloadSchema = ResolveOperationPayloadSchema(operation.Value);
                if (payloadSchema is null && !options.IncludeMethodsWithoutPayload) {
                    continue;
                }

                var payloadType = payloadSchema is null
                    ? resolver.ResolveOpenApiSchema(root, default, $"{endpointName}Payload")
                    : resolver.ResolveOpenApiSchema(root, payloadSchema.Value, $"{endpointName}Payload");

                var endpoint = new ContractEndpoint(
                    domain,
                    endpointName,
                    ContractEndpointKind.Operation,
                    options.DefaultDirection,
                    payloadType);
                mutation.AddContractEndpoint(contract, endpoint);
                endpoints.Add(endpoint);
            }
        }

        var analysis = mutation.Apply();
        return new ContractImportResult(contract, endpoints, resolver.CreatedTypes, analysis);
    }

    private static JsonElement? ResolveOperationPayloadSchema(JsonElement operation) {
        if (operation.TryGetProperty("requestBody", out var requestBody)
            && requestBody.ValueKind == JsonValueKind.Object
            && requestBody.TryGetProperty("content", out var requestContent)
            && TryResolveJsonSchemaFromContent(requestContent, out var requestSchema)) {
            return requestSchema;
        }

        if (operation.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Object) {
            foreach (var response in responses.EnumerateObject()) {
                if (!response.Name.StartsWith("2", StringComparison.Ordinal)) {
                    continue;
                }

                if (response.Value.ValueKind != JsonValueKind.Object
                    || !response.Value.TryGetProperty("content", out var responseContent)) {
                    continue;
                }

                if (TryResolveJsonSchemaFromContent(responseContent, out var responseSchema)) {
                    return responseSchema;
                }
            }
        }

        return null;
    }

    private static bool TryResolveJsonSchemaFromContent(JsonElement content, out JsonElement schema) {
        schema = default;
        if (content.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (content.TryGetProperty("application/json", out var jsonContent)
            && jsonContent.ValueKind == JsonValueKind.Object
            && jsonContent.TryGetProperty("schema", out schema)
            && schema.ValueKind == JsonValueKind.Object) {
            return true;
        }

        foreach (var mediaType in content.EnumerateObject()) {
            if (mediaType.Value.ValueKind != JsonValueKind.Object) {
                continue;
            }

            if (mediaType.Value.TryGetProperty("schema", out schema) && schema.ValueKind == JsonValueKind.Object) {
                return true;
            }
        }

        return false;
    }

    private static string ResolveContractName(JsonElement root) {
        if (root.TryGetProperty("info", out var info)
            && info.ValueKind == JsonValueKind.Object
            && info.TryGetProperty("title", out var title)
            && title.ValueKind == JsonValueKind.String) {
            var rawTitle = title.GetString();
            if (!string.IsNullOrWhiteSpace(rawTitle)) {
                return $"{rawTitle}Contract";
            }
        }

        return "OpenApiContract";
    }

    private static string ResolveEndpointName(string path, string method, JsonElement operation, ContractImportOptions options) {
        var defaultName = operation.TryGetProperty("operationId", out var operationId)
                          && operationId.ValueKind == JsonValueKind.String
            ? operationId.GetString() ?? $"{method}_{path}"
            : $"{method}_{path}";

        defaultName = defaultName
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal);

        return options.EndpointNameTransform?.Invoke(defaultName) ?? defaultName;
    }
}