using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    public class Cookie {
        public string Name { get; set; }
        public string Value { get; set; }

        public string Domain { get; set; }
        public string Path { get; set; }

        public DateTime Expires { get; set; }

        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }

        public override string ToString() {
            var result = new StringBuilder();

            result.Append(Name)
                  .Append('=')
                  .Append(Value);

            if (Domain != null)
                result.Append("; ")
                      .Append("domain=")
                      .Append(Domain);

            if (Path != null)
                result.Append("; ")
                      .Append("path=")
                      .Append(Path);

            if (Expires != default)
                result.Append("; ")
                      .Append("expires=")
                      .Append(Expires.ToHttpTimeString());

            if (HttpOnly)
                result.Append("; ")
                      .Append("HttpOnly");

            if (Secure)
                result.Append("; ")
                      .Append("Secure");

            return result.ToString();
        }

        public static Cookie Parse(string http_encoded) {
            var it = new StringIterator(http_encoded);
            it.SelectSplitSections("; ");

            var cookie = new Cookie();

            if (it.Goto('=')) {
                cookie.Name = it;
                it.Consume();
                cookie.Value = it;
            }

            if (it.IsLastSection)
                return cookie;

            it.ConsumeSection();

            // parse attributes
            return cookie;
        }

        public static IEnumerable<Cookie> ParseMultiple(string http_encoded) {
            var it = new StringIterator(http_encoded);
            it.SelectSplitSections("; ");

            while (!it.IsDone) {
                if (it.Goto('=')) {
                    var cookie = new Cookie();
                    cookie.Name = it;
                    it.Consume();
                    cookie.Value = it;
                    yield return cookie;
                }
                else {
                    throw new ArgumentException(it);
                }

                if (it.IsLastSection)
                    break;

                it.ConsumeSection();
            }
        }
    }
}