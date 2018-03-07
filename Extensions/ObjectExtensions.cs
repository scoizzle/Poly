namespace System {

    internal static class ObjectExtensions {

        public static bool TryToString<T>(this T obj, out string result) {
            try {
                result = obj.ToString();
                return true;
            }
            catch {
                result = null;
                return false;
            }
        }
    }
}