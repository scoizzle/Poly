using Poly.Data.Modeling;
using Poly.Data.Modeling.Recipes.Contracts;
using Poly.Data.Modeling.Recipes.Contracts.OpenApi;
using Poly.Data.Modeling.TypeSystem;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling.Recipes;

public class GitHubRestApiIntegrationTests {
    private const string GitHubRestApiSubset = """
        {
          "openapi": "3.0.3",
          "info": {
            "title": "GitHub v3 REST API",
            "version": "1.1.4"
          },
          "servers": [{ "url": "https://api.github.com" }],
          "paths": {
            "/repos/{owner}/{repo}": {
              "get": {
                "operationId": "repos/get",
                "responses": {
                  "200": {
                    "description": "Response",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Repository" }
                      }
                    }
                  }
                }
              }
            },
            "/repos/{owner}/{repo}/issues": {
              "get": {
                "operationId": "issues/list-for-repo",
                "responses": {
                  "200": {
                    "description": "Response",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "array",
                          "items": { "$ref": "#/components/schemas/Issue" }
                        }
                      }
                    }
                  }
                }
              },
              "post": {
                "operationId": "issues/create",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/IssueCreateRequest" }
                    }
                  }
                },
                "responses": {
                  "201": {
                    "description": "Created",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Issue" }
                      }
                    }
                  }
                }
              }
            },
            "/": {
              "get": {
                "operationId": "meta/root",
                "responses": {
                  "200": {
                    "description": "Response",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Root" }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Root": {
                "type": "object",
                "properties": {
                  "current_user_url": { "type": "string" },
                  "rate_limit_url": { "type": "string" }
                }
              },
              "User": {
                "type": "object",
                "properties": {
                  "login": { "type": "string" },
                  "id": { "type": "integer", "format": "int64" }
                }
              },
              "Repository": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "name": { "type": "string" },
                  "full_name": { "type": "string" },
                  "private": { "type": "boolean" },
                  "owner": { "$ref": "#/components/schemas/User" }
                }
              },
              "Issue": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer", "format": "int64" },
                  "number": { "type": "integer", "format": "int32" },
                  "title": { "type": "string" },
                  "state": { "type": "string" },
                  "user": { "$ref": "#/components/schemas/User" }
                }
              },
              "IssueCreateRequest": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "body": { "type": "string" },
                  "assignee": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    [Test]
    public async Task GitHubRestApiImport_CreatesExpectedContractAndSchemas() {
        var domain = BuildDomain();
        var recipe = new OpenApiContractImportRecipe();

        var result = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(GitHubRestApiSubset, "1.1.4"),
            new ContractImportOptions { ContractName = "GitHubRestContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(result.Contract.Name).IsEqualTo("GitHubRestContract");
        await Assert.That(result.Endpoints.Count).IsEqualTo(4);
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name == "repos_get")).IsTrue();
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name == "issues_list_for_repo")).IsTrue();
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name == "issues_create")).IsTrue();
        await Assert.That(result.Endpoints.Any(endpoint => endpoint.Name == "meta_root")).IsTrue();
    }

    [Test]
    public async Task GitHubRestApiImport_ImportsRepositoryAndIssueTypes() {
        var domain = BuildDomain();
        var recipe = new OpenApiContractImportRecipe();

        var result = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(GitHubRestApiSubset, "1.1.4"),
            new ContractImportOptions { ContractName = "GitHubRestContract" });

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var repository = domain.RequireType("Repository");
        var issue = domain.RequireType("Issue");
        var issueCreateRequest = domain.RequireType("IssueCreateRequest");
        var root = domain.RequireType("Root");

        await Assert.That(repository).IsTypeOf<Entity>();
        await Assert.That(issue).IsTypeOf<Entity>();
        await Assert.That(issueCreateRequest).IsTypeOf<Entity>();
        await Assert.That(root).IsTypeOf<Entity>();

        var repositoryEntity = (Entity)repository;
        await Assert.That(repositoryEntity.FindProperty("FullName")).IsNotNull();
        await Assert.That(repositoryEntity.FindProperty("Owner")).IsNotNull();

        var issueEntity = (Entity)issue;
        await Assert.That(issueEntity.FindProperty("Title")).IsNotNull();
        await Assert.That(issueEntity.FindProperty("User")).IsNotNull();
    }

    [Test]
    public async Task GitHubRestApiImport_ModelsIssueListResponseAndLocalBinding() {
        var domain = BuildDomain();
        var recipe = new OpenApiContractImportRecipe();

        var importResult = recipe.ImportInto(
            domain,
            new ContractImportSource.OpenApiJson(GitHubRestApiSubset, "1.1.4"),
            new ContractImportOptions {
                ContractName = "GitHubRestContract",
                DefaultDirection = ContractEndpointDirection.Inbound
            });

        var contract = domain.RequireImportedContract("GitHubRestContract");
        var issueListEndpoint = contract.Endpoints.First(endpoint => endpoint.Name == "issues_list_for_repo");
        var repositoryEndpoint = contract.Endpoints.First(endpoint => endpoint.Name == "repos_get");

        await Assert.That(domain.FindType("issueslistforrepoPayloadCollection")).IsNotNull();
        await Assert.That(issueListEndpoint.Kind).IsEqualTo(ContractEndpointKind.Operation);

        var worker = new Entity(domain, "RepositorySyncWorker");
        var syncAction = new DomainAction(domain, "SyncRepository", worker);
        var repoPayload = new Property(domain, "repoPayload", domain.RequireType("Repository"));
        var binding = new ContractBinding(domain, "GitHubRepoBinding", contract, repositoryEndpoint, syncAction, repoPayload.Name);
        var fieldMap = new ContractFieldMap(domain, "RepositoryFullNameMap", "full_name", "RepositoryName");

        var analysis = domain.CreateMutation()
            .AddType(worker)
            .AddAction(worker, syncAction)
            .AddParameter(syncAction, repoPayload)
            .AddContractBinding(binding)
            .AddContractFieldMap(binding, fieldMap)
            .Apply(importResult.Analysis);

        var integrationDiagnostic = analysis.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);

        await Assert.That(integrationDiagnostic).IsNull();
    }

    private static Domain BuildDomain() {
        var domain = new Domain("GitHub Integration");
        var mutation = domain.CreateMutation();
        CanonicalBuiltInTypeCatalog.AddToMutation(mutation);
        _ = mutation.Apply();
        return domain;
    }
}