namespace Poly.SourceGenerators;

public class GenerateKeyTypeAttributeSource
{
    public const string Name = "Poly.GenerateKeyTypeAttribute";
    public const string Source =
        """
        namespace Poly;
        public enum KeyType
        {
            Class,
            Struct
        }

        public enum KeyValueType
        {
            Guid
        }

        [AttributeUsage(validOn: AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
        public sealed class GenerateKeyTypeAttribute : Attribute
        {
            public string Suffix { get; set; } = "Id";

            public KeyType KeyType { get; set; } = KeyType.Struct;

            public KeyValueType ValueType { get; set; } = KeyValueType.Guid;
        }
        """;
}
