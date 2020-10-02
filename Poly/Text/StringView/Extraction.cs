using System;

namespace Poly
{
    public static class StringViewExtraction
    {
        public static bool ExtractBetween(ref this StringView view, char open, char close, out StringView section, bool includeBraces = false)
        {
            var location = Searching.FindMatchingBracket(view.String, view.Index, view.LastIndex, open, close);

            if (location == -1)
            {
                section = default;
                return false;
            }

            section = includeBraces ?
                view.Clone(lastIndex: location + 1) :
                view.Clone(index: view.Index + 1, lastIndex: location);

            view.Index = location + 1;
            return true;
        }

        public static bool ExtractStringLiteral(ref this StringView view, out StringView section, bool includeQuotes = false)
            => ExtractBetween(ref view, '"', '"', out section, includeQuotes);

        public static bool Extract(ref this StringView view, out sbyte value)
            => StringInt8Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out byte value)
            => StringInt8Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out short value)
            => StringInt16Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out ushort value)
            => StringInt16Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out int value)
            => StringInt32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out uint value)
            => StringInt32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out long value)
            => StringInt64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out ulong value)
            => StringInt64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out float value)
            => StringFloat32Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out double value)
            => StringFloat64Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out decimal value)
            => StringFloat128Parser.TryParse(view.String, ref view.Index, view.LastIndex, out value);

        public static bool Extract(ref this StringView view, out bool value, StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (StringViewConsumption.Consume(ref view, "false", stringComparison))
            {
                value = false;
                return true;
            }

            if (StringViewConsumption.Consume(ref view, "true", stringComparison))
            {
                value = true;
                return true;
            }

            value = default;
            return false;
        }
    }
}