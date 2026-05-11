using Poly.Data.Modeling;
using Poly.Data.Modeling.Recipes.Contracts;
using Poly.Data.Modeling.Recipes.Contracts.OpenApi;
using Poly.Data.Modeling.TypeSystem;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling.Recipes;

public class OpenApiPetstoreIntegrationTests {
    private const string SwaggerPetstoreSubset = """
        {
          "openapi": "3.0.4",
          "info": {
            "title": "Swagger Petstore - OpenAPI 3.0",
            "version": "1.0.27"
          },
          "servers": [{ "url": "/api/v3" }],
          "paths": {
            "/pet": {
              "put": {
                "operationId": "updatePet",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Successful operation",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                    }
                  }
                }
              },
              "post": {
                "operationId": "addPet",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "Successful operation",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                    }
                  }
                }
              }
            },
            "/pet/findByStatus": {
              "get": {
                "operationId": "findPetsByStatus",
                "responses": {
                  "200": {
                    "description": "successful operation",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": { "$ref": "#/components/schemas/Pet" }
                        }
                      }
                    }
                  }
                }
              }
            },
            "/pet/{petId}": {
              "get": {
                "operationId": "getPetById",
                "responses": {
                  "200": {
                    "description": "successful operation",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                    }
                  }
                }
              }
            },
            "/store/order": {
              "post": {
                "operationId": "placeOrder",
                "requestBody": {
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Order" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "successful operation",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/Order" } }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Category": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "name": { "type": "string" }
                }
              },
              "Pet": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "name": { "type": "string" },
                  "category": { "$ref": "#/components/schemas/Category" },
                  "tags": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/Category" }
                  },
                  "status": { "type": "string" }
                }
              },
              "Order": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "petId": { "type": "integer", "format": "int64" },
                  "quantity": { "type": "integer", "format": "int32" },
                  "status": { "type": "string" },
                  "complete": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    [Test]
    public async Task OpenApiPetstoreImport_CreatesContractAndExpectedEndpoints() {
        var domain = BuildDomainWithCanonicalPrimitives();
        var recipe = new OpenApiContractImportRecipe();

        var result = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(SwaggerPetstoreSubset, "1.0.27"),
            new ContractImportOptions { ContractName = "SwaggerPetstoreContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(result.Contract.Name).IsEqualTo("SwaggerPetstoreContract");
        await Assert.That(result.Endpoints.Count).IsEqualTo(5);
        await Assert.That(result.Endpoints.Any(ep => ep.Name == "addPet")).IsTrue();
        await Assert.That(result.Endpoints.Any(ep => ep.Name == "updatePet")).IsTrue();
        await Assert.That(result.Endpoints.Any(ep => ep.Name == "findPetsByStatus")).IsTrue();
        await Assert.That(result.Endpoints.Any(ep => ep.Name == "getPetById")).IsTrue();
        await Assert.That(result.Endpoints.Any(ep => ep.Name == "placeOrder")).IsTrue();
    }

    [Test]
    public async Task OpenApiPetstoreImport_CreatesKnownSchemaTypes() {
        var domain = BuildDomainWithCanonicalPrimitives();
        var recipe = new OpenApiContractImportRecipe();

        var result = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(SwaggerPetstoreSubset, "1.0.27"),
            new ContractImportOptions { ContractName = "SwaggerPetstoreContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var petType = domain.RequireType("Pet");
        var orderType = domain.RequireType("Order");
        var categoryType = domain.RequireType("Category");

        await Assert.That(petType).IsTypeOf<Entity>();
        await Assert.That(orderType).IsTypeOf<Entity>();
        await Assert.That(categoryType).IsTypeOf<Entity>();

        var petEntity = (Entity)petType;
        await Assert.That(petEntity.FindProperty("Name")).IsNotNull();
        await Assert.That(petEntity.FindProperty("Category")).IsNotNull();
        await Assert.That(petEntity.FindProperty("Tags")).IsNotNull();
        await Assert.That(petEntity.FindProperty("Status")).IsNotNull();
    }

    [Test]
    public async Task OpenApiPetstoreImport_AllowsBindingToLocalAction() {
        var domain = BuildDomainWithCanonicalPrimitives();
        var recipe = new OpenApiContractImportRecipe();
        var importResult = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(SwaggerPetstoreSubset, "1.0.27"),
            new ContractImportOptions {
                ContractName = "SwaggerPetstoreContract",
                DefaultDirection = ContractEndpointDirection.Inbound
            });

        var worker = new Entity(domain, "PetImportWorker");
        var importAction = new DomainAction(domain, "ImportPet", worker);
        var petPayloadParameter = new Property(domain, "petPayload", domain.RequireType("Pet"));
        var contract = domain.RequireImportedContract("SwaggerPetstoreContract");
        var endpoint = contract.Endpoints.First(ep => ep.Name == "addPet");
        var binding = new ContractBinding(domain, "PetstoreAddPetBinding", contract, endpoint, importAction, petPayloadParameter.Name);

        var analysis = domain.CreateMutation()
            .AddType(worker)
            .AddAction(worker, importAction)
            .AddParameter(importAction, petPayloadParameter)
            .AddContractBinding(binding)
            .AddContractFieldMap(binding, new ContractFieldMap(domain, "PetIdMap", "id", "ExternalPetId"))
            .AddContractFieldMap(binding, new ContractFieldMap(domain, "PetNameMap", "name", "ExternalPetName"))
            .Apply(importResult.Analysis);

        var integrationDiagnostic = analysis.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);

        await Assert.That(integrationDiagnostic).IsNull();
    }

    private static Domain BuildDomainWithCanonicalPrimitives() {
        var domain = new Domain("Petstore Integration");
        var mutation = domain.CreateMutation();
        CanonicalBuiltInTypeCatalog.AddToMutation(mutation);
        _ = mutation.Apply();
        return domain;
    }
}