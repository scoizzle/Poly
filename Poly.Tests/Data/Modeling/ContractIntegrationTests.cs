using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Recipes.Contracts;
using Poly.Data.Modeling.Recipes.Contracts.OpenApi;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class ContractIntegrationTests {
    private const string FakeProviderOpenApi = """
        {
          "openapi": "3.0.3",
          "info": { "title": "FakeShippingProvider" },
          "servers": [{ "url": "https://api.fake-shipping.local" }],
          "paths": {
            "/shipments": {
              "post": {
                "operationId": "createShipment",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/CreateShipmentRequest" }
                    }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/CreateShipmentResponse" }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "CreateShipmentRequest": {
                "type": "object",
                "properties": {
                  "orderId": { "type": "string", "format": "uuid" },
                  "postalCode": { "type": "string" }
                }
              },
              "CreateShipmentResponse": {
                "type": "object",
                "properties": {
                  "shipmentId": { "type": "string", "format": "uuid" },
                  "success": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    [Test]
    public async Task ContractBinding_ValidConfiguration_DoesNotReportContractIntegrationError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "SyncTicket", entity);
        var payload = new Property(domain, "payload", stringType);
        var contract = new ImportedContract(domain, "CrmContract", ContractSourceKind.ExternalProvider, "crm://contracts/ticket", "v1");
        var endpoint = new ContractEndpoint(domain, "UpdateTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, stringType);
        var binding = new ContractBinding(domain, "CrmTicketSync", contract, endpoint, action, "payload");

        var analysis = domain.CreateMutation()
            .AddType(stringType)
            .AddType(entity)
            .AddAction(entity, action)
            .AddParameter(action, payload)
            .AddImportedContract(contract)
            .AddContractEndpoint(contract, endpoint)
            .AddContractBinding(binding)
            .Apply();

        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task ContractBinding_EndpointNotOwnedByContract_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "SyncTicket", entity);
        var payload = new Property(domain, "payload", stringType);
        var contractA = new ImportedContract(domain, "CrmContract", ContractSourceKind.ExternalProvider, "crm://contracts/ticket", "v1");
        var contractB = new ImportedContract(domain, "ErpContract", ContractSourceKind.ExternalProvider, "erp://contracts/ticket", "v1");
        var endpointA = new ContractEndpoint(domain, "UpdateTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, stringType);
        var binding = new ContractBinding(domain, "TicketSync", contractB, endpointA, action, "payload");

        var analysis = domain.CreateMutation()
            .AddType(stringType)
            .AddType(entity)
            .AddAction(entity, action)
            .AddParameter(action, payload)
            .AddImportedContract(contractA)
            .AddImportedContract(contractB)
            .AddContractEndpoint(contractA, endpointA)
            .AddContractBinding(binding)
            .Apply();

        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);
        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task ContractBinding_MissingLocalParameter_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "SyncTicket", entity);
        var contract = new ImportedContract(domain, "CrmContract", ContractSourceKind.ExternalProvider, "crm://contracts/ticket", "v1");
        var endpoint = new ContractEndpoint(domain, "UpdateTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, stringType);
        var binding = new ContractBinding(domain, "TicketSync", contract, endpoint, action, "missing");

        var analysis = domain.CreateMutation()
            .AddType(stringType)
            .AddType(entity)
            .AddAction(entity, action)
            .AddImportedContract(contract)
            .AddContractEndpoint(contract, endpoint)
            .AddContractBinding(binding)
            .Apply();

        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Contract.IntegrationFragment);
    }

    [Test]
    public async Task ContractBinding_FakeOpenApiProvider_WiredThroughDomain_DoesNotReportError() {
        var domain = new Domain("Support");
        SeedCorePrimitiveTypes(domain);

        var importer = new OpenApiContractImportRecipe();
        var importResult = importer.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(FakeProviderOpenApi, "v1"),
            new ContractImportOptions {
                ContractName = "FakeShippingContract",
                DefaultDirection = ContractEndpointDirection.Inbound
            });

        var payloadType = domain.RequireType("CreateShipmentRequest");
        var contract = domain.RequireImportedContract("FakeShippingContract");
        var endpoint = contract.Endpoints.First(ep => ep.Name == "createShipment");

        var order = new Entity(domain, "Order");
        var syncAction = new DomainAction(domain, "SyncShipment", order);
        var payloadParameter = new Property(domain, "providerPayload", payloadType);
        var binding = new ContractBinding(domain, "FakeShippingSync", contract, endpoint, syncAction, payloadParameter.Name);

        var fieldMap1 = new ContractFieldMap(domain, "OrderIdMap", "orderId", "ExternalOrderId");
        var fieldMap2 = new ContractFieldMap(domain, "PostalCodeMap", "postalCode", "PostalCode");

        var analysis = domain.CreateMutation()
            .AddType(order)
            .AddAction(order, syncAction)
            .AddParameter(syncAction, payloadParameter)
            .AddContractBinding(binding)
            .AddContractFieldMap(binding, fieldMap1)
            .AddContractFieldMap(binding, fieldMap2)
            .Apply(importResult.Analysis);

        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task ContractBinding_FakeOpenApiProvider_EmptyFieldMapLocalName_ReportsError() {
        var domain = new Domain("Support");
        SeedCorePrimitiveTypes(domain);

        var importer = new OpenApiContractImportRecipe();
        var importResult = importer.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(FakeProviderOpenApi, "v1"),
            new ContractImportOptions { ContractName = "FakeShippingContract" });

        var payloadType = domain.RequireType("CreateShipmentRequest");
        var contract = domain.RequireImportedContract("FakeShippingContract");
        var endpoint = contract.Endpoints.First(ep => ep.Name == "createShipment");

        var order = new Entity(domain, "Order");
        var syncAction = new DomainAction(domain, "SyncShipment", order);
        var payloadParameter = new Property(domain, "providerPayload", payloadType);
        var binding = new ContractBinding(domain, "FakeShippingSync", contract, endpoint, syncAction, payloadParameter.Name);
        var invalidMap = new ContractFieldMap(domain, "InvalidMap", "orderId", "");

        var analysis = domain.CreateMutation()
            .AddType(order)
            .AddAction(order, syncAction)
            .AddParameter(syncAction, payloadParameter)
            .AddContractBinding(binding)
            .AddContractFieldMap(binding, invalidMap)
            .Apply(importResult.Analysis);

        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("field map", StringComparison.OrdinalIgnoreCase);
    }

    private static void SeedCorePrimitiveTypes(Domain domain) {
        var mutation = domain.CreateMutation();
        CanonicalBuiltInTypeCatalog.AddToMutation(mutation);
        _ = mutation.Apply();
    }
}