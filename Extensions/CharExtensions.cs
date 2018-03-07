using System.Text;

namespace System {

    internal static class CharExtensions {

        public static string ToHexString(this char C) =>
            ToHexString(Poly.App.Encoding, C);

        public static string ToHexString(this char C, Encoding Enc) =>
            ToHexString(Enc, C);

        public static string ToHexString(Encoding Encoding, params char[] Chars) =>
            Encoding.GetBytes(Chars).ToHexString();

        public static bool Compare(this char C, char S) =>
            (C - S) == 0;

        public static bool CompareInvariant(this char C, char S) {
            if (Compare(C, S))
                return true;

            if (C >= 'A' && C <= 'Z') {
                if (S >= 'a' && S <= 'z')
                    return C - 'A' == S - 'a';
            }
            else {
                if (S >= 'A' && S <= 'Z')
                    return C - 'a' == S - 'A';
            }

            return false;
        }
    }
}