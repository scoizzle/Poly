namespace Poly.Serialization;

using Poly.Reflection;

public static class JsonSerializer {
    public static string? Serialize<T>(T obj) {
        using var _ = Instrumentation.StartActivity();

        var writer = new JsonWriter();
        var typeInterface = TypeInterfaceRegistry.Get<T>()!;

        return typeInterface.Serialize(writer, obj)
            ? writer.Text.ToString()
            : default;
    }

    public static T? Deserialize<T>(string text) {
        using var _ = Instrumentation.StartActivity();

        var reader = new JsonReader(text);
        var typeInterface = TypeInterfaceRegistry.Get<T>()!;

        return typeInterface.Deserialize(reader, out var obj)
            ? obj
            : default;
    }
}