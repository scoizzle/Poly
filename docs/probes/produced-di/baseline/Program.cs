#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Poly.Generated;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<ShopDbContext>(options => options.UseSqlite("Data Source=shop.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db);
}

app.MapGet("/api/orders", async (ShopDbContext db) => await db.Orders.ToListAsync());

app.MapGet("/api/orders/{number}", async (string number, ShopDbContext db) => (await db.Orders.FirstOrDefaultAsync(e => e.Number == number) is Order order ? Results.Ok(order) : Results.NotFound()));

app.MapPost("/api/orders", async (OrderDto dto, ShopDbContext db) => {
    var orderResult = Order.Create(dto.Number);
    if (!orderResult.IsSuccess)
    {
        return Results.Conflict(orderResult.ErrorMessage);
    }
    db.Orders.Add(orderResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/orders/", orderResult.Value.Number.ToString()), orderResult.Value);
});

app.MapPost("/api/orders/{number}/pay", async (string number, PayDto dto, ShopDbContext db) => {
    var entity = await db.Orders.FirstOrDefaultAsync(e => e.Number == number);
    if (entity == null)
    {
        return Results.NotFound("Order not found");
    }
    try
    {
        var result = entity.Pay(dto.request);
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
});

static async System.Threading.Tasks.Task SeedAsync(ShopDbContext db)
{
    if (db == null)
    {
        return;
    }
    if (await db.Orders.AnyAsync())
    {
        return;
    }
    var orderResult = Order.Create("Sample");
    if (orderResult.IsSuccess)
    {
        db.Add(orderResult.Value);
    }
    await db.SaveChangesAsync();
}


app.Run();


public record OrderDto
{
    public string Number { get; init; } = default!;
}

public record PayDto
{
    public ChargeRequest request { get; init; } = default!;
}