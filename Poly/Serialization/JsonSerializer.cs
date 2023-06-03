namespace Poly.Serialization;

using Poly.Reflection;

public static class JsonSerializer {
    public static string? Serialize<T>(T obj) {
        Guard.IsNotNull(obj);

        using var _ = Activities.Source.StartActivity();

        var writer = new JsonWriter();
        var typeInterface = TypeInterfaceRegistry.Get<T>()!;
        var serializationDelegate = typeInterface.Serialize;

        return serializationDelegate(writer, obj)
            ? writer.Text.ToString()
            : default;
    }

    public static T? Deserialize<T>(string text) {
        Guard.IsNotNullOrEmpty(text);

        using var _ = Activities.Source.StartActivity();

        var reader = new JsonReader(text);
        var typeInterface = TypeInterfaceRegistry.Get<T>()!;
        var deserializationDelegate = typeInterface.Deserialize;

        return deserializationDelegate(reader, out var obj)
            ? obj
            : default;
    }
}