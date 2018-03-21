using System;

namespace Poly.Net.Http {
    using Collections;

    public partial class HeaderStorage {
        public class CookieStorage : KeyValueCollection<Cookie> {
            HeaderStorage.KeyValuePair cookie, set_cookie;

            public CookieStorage(HeaderStorage headers) {
                cookie = headers.GetStorage("Cookie");

                cookie.OnGet = cookie_get;
                cookie.OnSet = cookie_set;

                set_cookie = headers.GetStorage("Set-Cookie");

                set_cookie.OnGet = set_cookie_get;
                set_cookie.OnSet = set_cookie_set;
            }

            public bool Add(Cookie cookie) {
                return Add(cookie.Name, cookie);
            }

            public bool Add(string key, string value) {
                return Add(key, new Cookie { Name = key, Value = value });
            }

            string cookie_get() {
                return string.Join("; ", Values);
            }

            void cookie_set(string next) {
                foreach (var parsed in Cookie.ParseMultiple(next))
                    Set(parsed.Name, parsed);
            }

            string set_cookie_get() {
                return null;
            }

            void set_cookie_set(string next) {
                var parsed = Cookie.Parse(next);
                if (parsed == null) return;

                Set(parsed.Name, parsed);
            }
        }
    }
}