using System;
using System.Reflection;
using System.Diagnostics;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Microsoft.Extensions.Hosting;

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Microsoft.Extensions.DependencyInjection;

using Poly;
using Poly.Text.View.Benchmarks;

var serviceName = "Poly.Benchmarks";
var serviceVersion = "1.0.0";

var builder = new HostApplicationBuilder();

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource(serviceName, Instrumentation.Source.Name)
        .ConfigureResource(c => c
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk()
        )
        .AddOtlpExporter()
    );

builder.Services.AddLogging();

var host = builder.Build();

await host.StartAsync();

Instrumentation.StartActivity();

BenchmarkSwitcher
   .FromAssembly(assembly: Assembly.GetExecutingAssembly())
    .Run(args, DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance)).WithOptions(ConfigOptions.DisableOptimizationsValidator));

await host.StopAsync();