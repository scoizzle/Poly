using System.Text;

namespace Poly
{
    public static class StringBuilderExtensions
    {
        public static StringBuilder Append(this StringBuilder stringBuilder, StringView it)
            => stringBuilder.Append(it.String, it.Index, it.Length);

        public static StringBuilder AppendStringLiteral(this StringBuilder stringBuilder, string str)
            => stringBuilder.Append('"')
                            .Append(str)
                            .Append('"');

        public static StringBuilder AppendStringLiteral(this StringBuilder stringBuilder, StringView it)
            => stringBuilder.Append('"')
                            .Append(it.String, it.Index, it.Length)
                            .Append('"');
    }
}