using System;
using System.Linq;
using System.Text;

namespace Poly.Net {

    using Data;

    public class UrlQuery : KeyValueCollection<string> {

        public static Serializer<UrlQuery> Serializer = new Serializer<UrlQuery>(
            Serialize,
            Deserialize
            );

        private static bool Serialize(StringBuilder it, UrlQuery query) {
            var pairs = query.KeyValuePairs.ToArray();
            var last = pairs.Length - 1;

            for (var i = 0; i <= last; i++) {
                var pair = pairs[i];

                var key = Uri.EscapeDataString(pair.Key);
                var value = Uri.EscapeDataString(pair.Value);

                it.Append(key).Append('=').Append(value);

                if (i != last) {
                    it.Append('&');
                }
            }

            return true;
        }

        private static bool Deserialize(StringIterator it, out UrlQuery query) {
            query = new UrlQuery();

            while (it.SelectSection('=')) {
                string key = it;
                it.ConsumeSection();
                it.SelectSection('&');

                string value = it;
                it.ConsumeSection();

                query.Set(key, value);

                if (it.IsDone)
                    return true;
            }

            query = null;
            return false;
        }

        public static UrlQuery Parse(string text) {
            return Serializer.Deserialize(text);
        }

        public override string ToString() {
            return Serializer.Serialize(this);
        }
    }
}