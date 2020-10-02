using System;

namespace Poly.Serialization {
    public static class JsonSerializer {
        public static string Serialize<T>(T obj) {
            if (obj is null) throw new ArgumentNullException(nameof(obj));

            var typeInterface = Reflection.TypeInterface<T>.Get();
            var writer = new Serialization.JsonWriter();

            if (!typeInterface.Serialize(writer, obj)) return default;
            
            return writer.Text.ToString();
        }

        public static T Deserialize<T>(string text) {
            if (text is null) throw new ArgumentNullException(nameof(text));

            var typeInterface = Reflection.TypeInterface<T>.Get();
            var reader = new Serialization.JsonReader(text);

            if (typeInterface.Deserialize(reader, out var obj)) return obj;

            return default;
        }
    }
}