using System;
using System.Buffers;
using System.Diagnostics;

using Poly.Parsing;
using Poly.Parsing.Json;
using Poly.Serialization;
using Poly.Reflection;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Parsing.Benchmarks;

[MemoryDiagnoser, MinColumn, MaxColumn]
public class LoggingBenchmarks 
{
    _Log Log;
    LogToConsoleListener toConsoleListener;


    [GlobalSetup]
    public void Setup()
    {
        toConsoleListener = new();
        Log = new();
        Log.TryRegisterListener(toConsoleListener);
    }

    // [Benchmark]
    // public void Log_Baseline()
    // {
    //     _Log.Test();
    // }

    [Benchmark]
    public void Log_Event()
    {
        using var _ = Log.EventStarted();
    }

    [Benchmark]
    public void Log_Event_ManualName()
    {
        using var _ = Log.EventStarted("EventName");
    }
}