namespace Poly.Serialization;

using Poly.Reflection;

public static class JsonSerializer
{
    public static string? Serialize<T>(T obj)
    {
        var writer = new JsonWriter();
        var typeInterface = TypeAdapterRegistry.Get<T>()!;

        return typeInterface.Serialize(writer, obj)
            ? writer.Text.ToString()
            : default;
    }

    public static T? Deserialize<T>(string text)
    {
        var reader = new JsonReader(text);
        var typeInterface = TypeAdapterRegistry.Get<T>()!;

        return typeInterface.Deserialize(reader, out var obj)
            ? obj
            : default;
    }
}