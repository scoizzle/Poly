using System.Text;

namespace System {
    using Text;

    public static class CharExtensions {

        public static string ToHexString(this char C) =>
            ToHexString(Poly.App.Encoding, C);

        public static string ToHexString(this char C, Encoding Enc) =>
            ToHexString(Enc, C);

        public static string ToHexString(Encoding Encoding, params char[] Chars) =>
            Encoding.GetBytes(Chars).ToHexString();

        public static bool Compare(this char left, char right) =>
            (left - right) == 0;

        public static bool CompareIgnoreCase(this char left, char right) {
            if (Compare(left, right))
                return true;

            if (left >= 'A' && left <= 'Z') {
                if (right >= 'a' && right <= 'z')
                    return left - 'A' == right - 'a';
            }
            else {
                if (right >= 'A' && right <= 'Z')
                    return left - 'a' == right - 'A';
            }

            return false;
        }
    }
}