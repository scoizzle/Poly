var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Poly_Web_ApiService>("apiservice");

builder.AddProject<Projects.Poly_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
