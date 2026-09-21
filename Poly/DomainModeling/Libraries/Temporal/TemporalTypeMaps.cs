using Poly.DomainModeling.Meaning;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>Host type mappings for temporal catalog types. Core does not name them.</summary>
public static class TemporalTypeMaps {
    public static void Apply(TypeMappingRegistry maps) {
        ArgumentNullException.ThrowIfNull(maps);
        maps.Register(DateTime("DateTime"));
        maps.Register(DateTime("Timestamp"));
        maps.Register(Date("Date"));
        maps.Register(Date("DateOnly"));
        maps.Register(Time("Time"));
        maps.Register(Time("TimeOnly"));
        maps.Register(Duration("Duration"));
        maps.Register(Duration("TimeSpan"));
    }

    private static HostTypeMapping DateTime(string name) =>
        new(name, "DateTime", "timestamp", Prim.DateTime, true, "DateTime", "MinValue");

    private static HostTypeMapping Date(string name) =>
        new(name, "DateOnly", "date", Prim.DateOnly, true, "DateOnly", "MinValue");

    private static HostTypeMapping Time(string name) =>
        new(name, "TimeOnly", "time", Prim.TimeOnly, true, "TimeOnly", "MinValue");

    private static HostTypeMapping Duration(string name) =>
        new(name, "TimeSpan", "interval", Prim.TimeSpan, true, "TimeSpan", "Zero");
}