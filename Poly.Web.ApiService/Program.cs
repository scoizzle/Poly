using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    Tenant.Dto test = new();
    return test;
})
.RequireAuthorization([Tenant.ReadPolicy]);

app.MapDefaultEndpoints();

app.Run();

public abstract record DtoBase
{
    [JsonPropertyName(name: "_links")]
    public Dictionary<string, Link> Links { get; init; } = new();
}

public record Link(string Href, HttpMethod Method);

public static partial class Tenant
{
    public const string ReadPolicy = "tenant.read";
    public const string UpdatePolicy = "tenant.update";

    public record struct Id(long Value) { public Id() : this(Random.Shared.NextInt64(1000000, 9999999)) { } }

    public record Model(Id Id, string Name, ICollection<Child.Model> Children)
    {
        private Model() : this(default, default!, default!) { }
    }

    public record QueryFilter(
        Id? Id = default,
        string? Name = default,
        bool IncludeChildren = false
        );

    private static Func<TenantDbContext, QueryFilter, IAsyncEnumerable<Model>> _withQueryFilterCompiledQuery = EF.CompileAsyncQuery(
        (TenantDbContext context, QueryFilter filter) => context.Tenants.WithQueryFilter(filter)
    );

    public static IQueryable<Model> WithQueryFilter(this IQueryable<Model> tenants, QueryFilter filter)
        => tenants
            .Include(t => t.Children.Where(f => filter.IncludeChildren))
            .Where(t => !filter.Id.HasValue || t.Id == filter.Id)
            .Where(t => string.IsNullOrWhiteSpace(filter.Name) || EF.Functions.Like(t.Name, $"%{filter.Name}%"));

    public class ViewModel(
        TenantDbContext context)
    {
        private Model? _model;

        public async Task LoadAsync(Id id, CancellationToken cancellationToken = default)
        {
            _model = await context.Tenants.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }
    }

    public static Model CreateNewTenant(this TenantDbContext context, string name)
    {
        var tenant = new Model(new Id(), name, new List<Child.Model>());
        context.Tenants.Add(tenant);
        return tenant;
    }

    [AutoIncludeLink([ReadPolicy, UpdatePolicy])]
    public record Dto : DtoBase
    {
        internal Dto() { Id = 0; Name = string.Empty; Models = []; }
        public Dto(long id, string name, ICollection<Child.Dto> models) => (Id, Name, Models) = (id, name, models);

        public long Id { get; init; }
        public string Name { get; init; }

        [AutoIncludeLink]
        public ICollection<Child.Dto> Models { get; init; }
    }
}

public static class Child
{
    public record struct Id(long Value) { public Id() : this(Random.Shared.NextInt64(1000000, 9999999)) { } }

    public record Model(Id Id, Tenant.Model Tenant, string Identifier, string Name, int Age);

    public record Dto(long Id, string Identifier, string Name, int Age) : DtoBase;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
sealed class AutoIncludeLinkAttribute(string[]? policyNames = default) : Attribute
{
    public IEnumerable<string> PolicyNames { get; } = policyNames ?? [];
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class ResourceAttribute(string routePattern) : Attribute
{
    public string RoutePattern { get; } = routePattern;
}

public class TenantDbContext : DbContext
{
    public DbSet<Tenant.Model> Tenants => Set<Tenant.Model>();
    public DbSet<Child.Model> Children => Set<Child.Model>();
}