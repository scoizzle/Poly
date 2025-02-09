using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Poly.Web;

[GenerateKeyType(Suffix = "Key", KeyType = KeyType.Struct, ValueType = KeyValueType.Guid)]
public partial class Test(string field)
{
    static void TestSomething()
    {
        TestId Id = new();
    }
}

interface IEntity<T, TKey>
    where T : class
    where TKey : new()
{
    TKey Id { get; }
}

internal record RouteGroupConfiguration(
    string Name,
    IReadOnlyCollection<RouteGroupConfiguration> Groups,
    IReadOnlyCollection<RouteConfiguration> Routes)
{
    internal static RouteGroupConfiguration From(string name, List<FluentRouteConfigurationBuilder> groupBuilders, List<RouteConfiguration> routeConfigurations)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(groupBuilders);
        ArgumentNullException.ThrowIfNull(routeConfigurations);

        var groups = groupBuilders
            .Select(e => e.Build())
            .ToList();

        var routes = routeConfigurations.ToList();

        return new RouteGroupConfiguration(name, groups, routes);
    }
}

internal record RouteConfiguration(
    string Route,
    HttpMethod HttpMethod,
    Delegate RequestHandler);

public class FluentRouteConfigurationBuilder(string route)
{
    private readonly List<FluentRouteConfigurationBuilder> m_Groups = new();
    private readonly List<RouteConfiguration> m_Routes = new();

    public FluentRouteConfigurationBuilder MapGroup(string route, Action<FluentRouteConfigurationBuilder> configure)
    {
        FluentRouteConfigurationBuilder group = new(route);
        configure(group);
        m_Groups.Add(group);
        return this;
    }

    public FluentRouteConfigurationBuilder MapGet(string route, Delegate handler)
    {
        m_Routes.Add(new RouteConfiguration(route, HttpMethod.Get, handler));
        return this;
    }

    public FluentRouteConfigurationBuilder MapPost(string route, Delegate handler)
    {
        m_Routes.Add(new RouteConfiguration(route, HttpMethod.Post, handler));
        return this;
    }

    internal RouteGroupConfiguration Build() => RouteGroupConfiguration.From(route, m_Groups, m_Routes);
}

public static class A_Ext
{
    public static IServiceCollection AddFluentRouting(this IServiceCollection services, Action<FluentRouteConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        FluentRouteConfigurationBuilder routesBuilder = new(string.Empty);
        configure(routesBuilder);

        RouteGroupConfiguration routes = routesBuilder.Build();
        services.AddSingleton(routes);

        return services;
    }

    public static void MapFluentRoutes(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.ServiceProvider.GetService<RouteGroupConfiguration>();
        if (routes is null) return;

        MapGroup(routes);

        void MapGroup(RouteGroupConfiguration config)
        {
            RouteGroupBuilder groupBuilder = endpoints.MapGroup(config.Name);

            foreach (var child in config.Groups)
            {
                MapGroup(child);
            }

            foreach (var route in config.Routes)
            {
                groupBuilder.MapMethods(route.Route, [route.HttpMethod.Method], route.RequestHandler);
            }
        }
    }
}