using System.Collections.Generic;

namespace Poly.Net.Http {
    using Collections;

    public class CookieHeader : Header {
        public Dictionary<string, Cookie> Storage;

        public CookieHeader() : base("Cookie") {
            Storage = new Dictionary<string, Cookie>();
        }

        public Cookie this[string key] {
            get => Storage[key];
            set => Storage[key] = value;
        }

        public static string Serialize(Cookie cookie) {
            return $"{cookie.Name}={cookie.Value}";
        }

        public bool TryDeserialize(StringIterator it, out Cookie cookie) {
            if (it.SelectSection('=')) {
                var key = it.ToString();
                it.ConsumeSection();
                var value = it.ToString();

                cookie = new Cookie { Name = key, Value = value };
                return true;
            }

            cookie = default;
            return false;
        }
    }
}