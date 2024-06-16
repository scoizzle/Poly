using System.Reflection;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

BenchmarkSwitcher
    .FromAssembly(assembly: Assembly.GetExecutingAssembly())
    .Run(args, DefaultConfig.Instance.AddJob(Job.Default));