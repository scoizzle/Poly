using Microsoft.FluentUI.AspNetCore.Components;

using Poly.Demo.Shared.Services;
using Poly.Demo.Web.Components;
using Poly.Demo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

// Add device-specific services used by the Poly.Demo.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddSingleton<PolyIdeCompiler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(Poly.Demo.Shared._Imports).Assembly);

app.Run();