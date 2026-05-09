using Poly.Data.Modeling;
using Poly.Data.Modeling.Recipes.Contracts;
using Poly.Data.Modeling.Recipes.Contracts.Clr;
using Poly.Data.Modeling.Recipes.Contracts.OpenApi;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling.Recipes;

public class ContractImportRecipeTests {
    [Test]
    public async Task ClrContractImport_ImportsMethodsAndEvents() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new ClrContractImportRecipe();
        var source = new ContractImportSource.ClrType(typeof(SampleClrContract), "v1");

        var result = recipe.ImportInto(domain, source, new ContractImportOptions { ContractName = "ClrSupportContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(result.Contract.Name).IsEqualTo("ClrSupportContract");
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name.Contains("CreateTicket", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Kind == ContractEndpointKind.Event)).IsTrue();

        var requestType = domain.FindType(nameof(CreateTicketRequest));
        await Assert.That(requestType).IsNotNull();
        await Assert.That(requestType).IsTypeOf<Entity>();
    }

    [Test]
    public async Task OpenApiContractImport_ImportsOperationsAndPayloadSchema() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new OpenApiContractImportRecipe();
        var source = new ContractImportSource.OpenApiJson(
            """
            {
              "openapi": "3.0.3",
              "info": { "title": "SupportApi" },
              "paths": {
                "/tickets": {
                  "post": {
                    "operationId": "createTicket",
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": { "$ref": "#/components/schemas/TicketInput" }
                        }
                      }
                    },
                    "responses": {
                      "200": { "description": "ok" }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "TicketInput": {
                    "type": "object",
                    "properties": {
                      "title": { "type": "string" },
                      "priority": { "type": "integer" }
                    }
                  }
                }
              }
            }
            """,
            "v1");

        var result = recipe.ImportInto(domain, source);

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(result.Contract.Name).IsEqualTo("SupportApiContract");
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name == "createTicket")).IsTrue();

        var ticketInputType = domain.FindType("TicketInput");
        await Assert.That(ticketInputType).IsNotNull();
        await Assert.That(ticketInputType).IsTypeOf<Entity>();

        var entity = (Entity)ticketInputType!;
        await Assert.That(entity.FindProperty("title")).IsNotNull();
        await Assert.That(entity.FindProperty("priority")).IsNotNull();
    }

    [Test]
    public async Task OpenApiContractImport_AppliesConfiguredEndpointDirection() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new OpenApiContractImportRecipe();
        var source = new ContractImportSource.OpenApiJson(
            """
            {
              "openapi": "3.0.3",
              "info": { "title": "DirectionApi" },
              "paths": {
                "/ping": {
                  "get": {
                    "operationId": "ping",
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            "v1");

        var result = recipe.ImportInto(
            domain,
            source,
            new ContractImportOptions {
                DefaultDirection = ContractEndpointDirection.Inbound,
                ContractName = "DirectionContract"
            });

        await Assert.That(result.Endpoints.Count).IsEqualTo(1);
        await Assert.That(result.Endpoints[0].Direction).IsEqualTo(ContractEndpointDirection.Inbound);
    }

    [Test]
    public async Task OpenApiContractImport_DuplicateContractName_Throws() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new OpenApiContractImportRecipe();
        var source = new ContractImportSource.OpenApiJson(
            """
            {
              "openapi": "3.0.3",
              "info": { "title": "DuplicateApi" },
              "paths": {
                "/ping": {
                  "get": {
                    "operationId": "ping",
                    "responses": {
                      "200": { "description": "ok" }
                    }
                  }
                }
              }
            }
            """,
            "v1");

        _ = recipe.ImportInto(domain, source, new ContractImportOptions { ContractName = "DuplicateContract" });

        await Assert.That(() => recipe.ImportInto(domain, source, new ContractImportOptions { ContractName = "DuplicateContract" }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OpenApiContractImport_UnresolvedComponentReference_Throws() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new OpenApiContractImportRecipe();
        var source = new ContractImportSource.OpenApiJson(
            """
            {
              "openapi": "3.0.3",
              "info": { "title": "BrokenApi" },
              "paths": {
                "/broken": {
                  "post": {
                    "operationId": "createBroken",
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": { "$ref": "#/components/schemas/MissingSchema" }
                        }
                      }
                    },
                    "responses": {
                      "200": { "description": "ok" }
                    }
                  }
                }
              },
              "components": { "schemas": { } }
            }
            """,
            "v1");

        await Assert.That(() => recipe.ImportInto(domain, source, new ContractImportOptions { ContractName = "BrokenContract" }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OpenApiContractImport_ArraySchema_UsesStructuredModeling() {
        var domain = new Domain("Support");
        domain.AddType(new Primitive(domain, "Text", TypeCategory.Text | TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "Boolean", TypeCategory.Boolean));
        domain.AddType(new Primitive(domain, "Number", TypeCategory.Numeric | TypeCategory.Primitive));

        var recipe = new OpenApiContractImportRecipe();
        var source = new ContractImportSource.OpenApiJson(
            """
            {
              "openapi": "3.0.3",
              "info": { "title": "ArrayApi" },
              "paths": {
                "/items": {
                  "get": {
                    "operationId": "listItems",
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "array",
                              "items": { "$ref": "#/components/schemas/Item" }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Item": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "integer", "format": "int64" }
                    }
                  }
                }
              }
            }
            """,
            "v1");

        var result = recipe.ImportInto(domain, source, new ContractImportOptions { ContractName = "ArrayContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(domain.FindType("listItemsPayloadCollection")).IsNotNull();
        await Assert.That(domain.FindType("Item")).IsTypeOf<Entity>();
    }

    public interface SampleClrContract {
        CreateTicketResult CreateTicket(CreateTicketRequest request);
        event EventHandler<TicketCreatedEventArgs> TicketCreated;
    }

    public sealed record CreateTicketRequest(string Title, int Priority);
    public sealed record CreateTicketResult(Guid TicketId);
    public sealed class TicketCreatedEventArgs : EventArgs {
        public Guid TicketId { get; init; }
    }
}