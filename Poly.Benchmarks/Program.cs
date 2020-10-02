using System;
using System.Reflection;

using BenchmarkDotNet.Running;

namespace Poly.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(assembly: Assembly.GetExecutingAssembly()).Run(args);
        }
    }
}
