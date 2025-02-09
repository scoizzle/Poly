using Poly.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddFluentRouting(config => config
    .MapGroup("groups", configure: c => c
        .MapGet("/", handler: () => "Test")
    )
    .MapGroup("others", c => c
        .MapGet("/", () => "abc")
    )
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapFluentRoutes();

app.Run();