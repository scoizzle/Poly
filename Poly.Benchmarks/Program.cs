using System.Reflection;
using System.Diagnostics;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Poly.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        var serviceName = "Poly.Benchmarks";
        var serviceVersion = "1.0.0";

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(serviceName)
            .ConfigureResource(resource =>
                resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion))
            .AddConsoleExporter()
            .Build();

        BenchmarkSwitcher
            .FromAssembly(assembly: Assembly.GetExecutingAssembly())
            .Run(args, DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance)));
    }
}