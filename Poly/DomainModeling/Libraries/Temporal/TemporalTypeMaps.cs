using Poly.DomainModeling.Meaning;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>CLR/SQL projections for temporal catalog types. Core DomainTypeMapping does not name them.</summary>
public static class TemporalTypeMaps {
    public static void Apply(TypeMappingRegistry maps) {
        ArgumentNullException.ThrowIfNull(maps);
        maps.OverrideClrTypeName("DateTime", "DateTime");
        maps.OverrideClrTypeName("Timestamp", "DateTime");
        maps.OverrideClrTypeName("Date", "DateOnly");
        maps.OverrideClrTypeName("DateOnly", "DateOnly");
        maps.OverrideClrTypeName("Time", "TimeOnly");
        maps.OverrideClrTypeName("TimeOnly", "TimeOnly");
        maps.OverrideClrTypeName("Duration", "TimeSpan");
        maps.OverrideClrTypeName("TimeSpan", "TimeSpan");

        maps.OverrideSqlColumnType("DateTime", "timestamp");
        maps.OverrideSqlColumnType("Timestamp", "timestamp");
        maps.OverrideSqlColumnType("Date", "date");
        maps.OverrideSqlColumnType("DateOnly", "date");
        maps.OverrideSqlColumnType("Time", "time");
        maps.OverrideSqlColumnType("TimeOnly", "time");
        maps.OverrideSqlColumnType("Duration", "interval");
        maps.OverrideSqlColumnType("TimeSpan", "interval");
    }
}