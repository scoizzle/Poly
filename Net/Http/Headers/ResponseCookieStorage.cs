using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Data;
    using Collections;

    public class ResponseCookieStorage : ResponseHeaders.KeyArrayPair {
        public KeyValueCollection<Cookie> Storage;

        public ResponseCookieStorage(ResponseHeaders headers) : base("Set-Cookie") {
            SetStorage(headers);

            Storage = new KeyValueCollection<Cookie>();
        }

        public Cookie this[string key] {
            get => Storage[key];
            set => Storage[key] = value;
        }

        public bool Add(Cookie cookie) =>
            Storage.Add(cookie.Name, cookie);


        //public override string Value {
        //    get => default;
        //    set {
        //        var it = new StringIterator(value);
        //        it.SelectSplitSections("; ");

        //        if (TryDeserialize(it, out Cookie cookie))
        //            Storage.Set(cookie.Name, cookie);
        //    }
        //}

        public override IEnumerable<RequestHeaders.KeyValuePair> GetEnumerator() {
            foreach (var cookie in Storage) {
                yield return new RequestHeaders.KeyValuePair(Key, Serialize(cookie.Value));
            }
        }

        public static string Serialize(Cookie cookie) {
            var result = new StringBuilder();

            result.Append(cookie.Name)
                  .Append('=')
                  .Append(cookie.Value);

            var domain = cookie.Domain;
            if (domain != null)
                result.Append("; ")
                      .Append("domain=")
                      .Append(domain);

            var path = cookie.Path;
            if (path != null)
                result.Append("; ")
                      .Append("path=")
                      .Append(path);

            var expires = cookie.Expires;
            if (expires != default)
                result.Append("; ")
                      .Append("expires=")
                      .Append(expires.ToHttpTimeString());

            if (cookie.HttpOnly)
                result.Append("; ")
                      .Append("HttpOnly");

            if (cookie.Secure)
                result.Append("; ")
                      .Append("Secure");

            return result.ToString();
        }

        public bool TryDeserialize(StringIterator it, out Cookie cookie) {
            if (it.SelectSection('=')) {
                var key = it.ToString();
                it.ConsumeSection();
                var value = it.ToString();

                cookie = new Cookie { Name = key, Value = value };
                it.ConsumeSection();

                while (!it.IsDone) {
                    if (it.SelectSection('=')) {
                        if (it.ConsumeIgnoreCase("domain")) {
                            it.ConsumeSection();
                            cookie.Domain = it.ToString();
                        }
                        else
                        if (it.ConsumeIgnoreCase("path")) {
                            it.ConsumeSection();
                            cookie.Path = it.ToString();
                        }
                        else
                        if (it.ConsumeIgnoreCase("expires")) {
                            it.ConsumeSection();
                            cookie.Expires = it.ToString().FromHttpTimeString();
                        }
                        else break;
                    }
                    else {
                        if (it.ConsumeIgnoreCase("HttpOnly")) {
                            cookie.HttpOnly = true;
                        }
                        else
                        if (it.ConsumeIgnoreCase("Secure")) {
                            cookie.Secure = true;
                        }
                        else break;
                    }

                    if (it.IsLastSection)
                        return true;

                    it.ConsumeSection();
                }
            }

            cookie = default;
            return false;
        }
    }
}