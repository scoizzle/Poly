using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Poly.Web.Resources;

public static class ResourceMethods
{
    public const string GET = "GET";
    public const string POST = "POST";
    public const string PUT = "PUT";
    public const string DELETE = "DELETE";
    public const string HEAD = "HEAD";
    public const string OPTIONS = "OPTIONS";
    public const string TRACE = "TRACE";
    public const string PATCH = "PATCH";
}


[AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ResourceAttribute : Attribute
{
    public required string Route { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? AccessPolicy { get; init; }
    public string Method { get; init; } = ResourceMethods.GET;
}

[Resource(Name = "Tenants", Route = "api/[controller]", AccessPolicy = AccessPolicy, Method = "GET")]
class Test()
{
    public const string AccessPolicy = "tenants:read";
};

public record Company(Guid Id, string Name) : IEnumerable<Customer>
{
    public ICollection<Customer> Customers { get; init; } = new List<Customer>();

    public void Add(Customer customer)
    {
        Customers.Add(customer);
        customer.Company = this;
    }

    public IEnumerator<Customer> GetEnumerator() => Customers.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Customers.GetEnumerator();
}

public record Customer(Guid Id, string Name)
{
    public Company Company { get; internal set; } = default!;
}

static class DemoData
{
    public static Company Company1 { get; } = new Company(Guid.NewGuid(), "Company 1")
    {
        new Customer(Guid.NewGuid(), "Customer 1"),
        new Customer(Guid.NewGuid(), "Customer 2"),
    };
}

[Authorize]
[Route(template: Route)]
class CompanyResource : Controller
{
    public const string Route = "api/companies";
    public const string AccessPolicy = "companies:list";

    [HttpGet("/")]
    public async ValueTask<IActionResult> ListCompanies()
    {
        await Task.CompletedTask;
        return Ok(DemoData.Company1);
    }
}

record ResourceInfo(
    string Name,
    string AccessPolicy,
    string Route,
    IEnumerable<ResourcePropertyInfo> Properties,
    IEnumerable<ResourceActionInfo> Actions,
    IEnumerable<ChildResourceInfo> Children)
{

    public static ResourceInfo FromTypeInfo(Type type)
    {
        var propertyInfos = type
            .GetProperties()
            .Select(ResourcePropertyInfo.FromPropertyInfo)
            .ToList();

        var resourceAttribute = type.GetCustomAttribute<ResourceAttribute>();
        var name = resourceAttribute?.Name ?? type.Name.ToString();
        var accessPolicy = resourceAttribute?.AccessPolicy ?? string.Empty;
        var route = resourceAttribute?.Route ?? string.Empty;

        return new ResourceInfo(
            Name: name,
            AccessPolicy: accessPolicy,
            Route: route,
            Properties: propertyInfos,
            Actions: Array.Empty<ResourceActionInfo>(),
            Children: Array.Empty<ChildResourceInfo>()
            );
    }
}

record ResourcePropertyInfo(
    string Name,
    string AccessPolicy,
    Type Type)
{
    public static ResourcePropertyInfo FromPropertyInfo(PropertyInfo property)
    {
        var authorizeAttribute = property.GetCustomAttribute<AuthorizeAttribute>();
        var name = property.Name;
        var accessPolicy = authorizeAttribute?.Policy ?? string.Empty;
        var type = property.PropertyType;

        return new ResourcePropertyInfo(
            Name: name,
            AccessPolicy: accessPolicy,
            Type: type);
    }
}

record ResourceActionParameterInfo(
    string Name,
    Type Type);

record ResourceActionInfo(
    string Name,
    string AccessPolicy,
    Type ResultType,
    IEnumerable<ResourceActionParameterInfo> Parameters);

record ChildResourceInfo(
    string Name,
    string AccessPolicy,
    ResourceInfo Type);

abstract record ResourceResult
{
    public record StatusCodeResult(HttpStatusCode StatusCode)
        : ResourceResult;

    public sealed record OkResult<T>(T? Value)
        : StatusCodeResult(StatusCode: HttpStatusCode.OK);

    public sealed record ErrorResult(string? Message = default, Exception? Error = default)
        : StatusCodeResult(StatusCode: HttpStatusCode.InternalServerError);

    public sealed record NotFoundResult(string? Message = default)
        : StatusCodeResult(StatusCode: HttpStatusCode.NotFound);

    public sealed record UnauthorizedResult(string? Message = default)
        : StatusCodeResult(StatusCode: HttpStatusCode.Unauthorized);

    public sealed record BadRequestResult(string? Message = default)
        : StatusCodeResult(StatusCode: HttpStatusCode.BadRequest);

    public sealed record NoContentResult()
        : StatusCodeResult(StatusCode: HttpStatusCode.NoContent);

    public static OkResult<T> Ok<T>(T value) => new(value);
    public static ErrorResult Error<T>(string? message = default, Exception? exception = default) => new(message, exception);
    public static EmptyResult Empty() => new();
}

record ResourceInformationProvider()
{
    private ConcurrentDictionary<Type, ResourceInfo> _resourceInfoCache = new();

    public ResourceInfo GetResourceInfo<T>() => GetResourceInfo(type: typeof(T));
    public ResourceInfo GetResourceInfo(Type type) => _resourceInfoCache.GetOrAdd(type, _ => ResourceInfo.FromTypeInfo(type));
}
