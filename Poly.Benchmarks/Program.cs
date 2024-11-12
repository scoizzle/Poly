using System.Reflection;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Poly.Serialization;
using Poly.Serialization.Benchmarks.Serializer;
using System;
using Poly;


BenchmarkSwitcher
    .FromAssembly(assembly: Assembly.GetExecutingAssembly())
    .Run(args, new DebugInProcessConfig());