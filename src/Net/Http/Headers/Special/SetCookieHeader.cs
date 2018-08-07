using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Data;
    using Collections;

    public class SetCookieHeader : Header {
        public Dictionary<string, Cookie> Storage;

        public SetCookieHeader() : base("Set-Cookie") {
            Storage = new Dictionary<string, Cookie>();
        }

        public Cookie this[string key] {
            get => Storage.TryGetValue(key, out Cookie value) ? value : default;
            set => Storage[key] = value;
        }

        public override IEnumerable<string> Serialize() =>
            Storage.TrySelect(pair => Serialize(pair.Value));

        public override void Deserialize(StringIterator value) {
            if (TryDeserialize(value.Clone(), out Cookie cookie))
                Storage.Add(cookie.Name, cookie);
        }
        
        public override void Reset() =>
            Storage.Clear();

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

        public static bool TryDeserialize(StringIterator it, out Cookie cookie) {
            it.SelectSplitSections("; ");

            if (!it.SelectSection("="))
                goto format_error;
            
            cookie = new Cookie();
            it.ExtractSection(out cookie.Name);
            it.ExtractSection(out cookie.Value);

            while (!it.IsDone) {
                if (!it.SelectSection("="))
                    goto format_error;

                if (it.ConsumeSectionIgnoreCase("Domain")) {
                    it.ExtractSection(out cookie.Domain);
                }
                else
                if (it.ConsumeSectionIgnoreCase("Path")) {
                    it.ExtractSection(out cookie.Path);
                }
                else
                if (it.ConsumeSectionIgnoreCase("Expires")) {
                    it.ExtractSection(out string expires);

                    cookie.Expires = expires.FromHttpTimeString();
                }
                else
                if (it.ConsumeSectionIgnoreCase("Max-Age")) {
                    if (!it.ExtractSection(out uint seconds))
                        goto format_error;

                    cookie.Expires = DateTime.Now.AddSeconds(seconds);
                }
                else
                if (it.ConsumeSectionIgnoreCase("HttpOnly")) {
                    cookie.HttpOnly = true;
                }
                else
                if (it.ConsumeSectionIgnoreCase("Secure")) {
                    cookie.Secure = true;
                }
                else goto format_error;
            }
            while (!it.IsLastSection);

            return true;

        format_error:
            cookie = default;
            return false;
        }
    }
}