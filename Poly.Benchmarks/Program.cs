using BenchmarkDotNet.Running;

using Poly.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;