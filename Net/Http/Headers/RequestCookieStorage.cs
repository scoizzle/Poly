using System.Collections.Generic;

namespace Poly.Net.Http {
    using Collections;

    public class RequestCookieStorage : RequestHeaders.KeyArrayPair {
        public KeyValueCollection<Cookie> Storage;

        public RequestCookieStorage(RequestHeaders headers) : base("Cookie") {
            SetStorage(headers);
            Storage = new KeyValueCollection<Cookie>();
        }

        public Cookie this[string key] {
            get => Storage[key];
            set => Storage[key] = value;
        }

        //public override string Value {
        //    get => default;
        //    set {
        //        var it = new StringIterator(value);
        //        it.SelectSplitSections("; ");

        //        while (!it.IsDone) {
        //            if (!TryDeserialize(it, out Cookie cookie))
        //                break;

        //            Storage.Add(cookie.Name, cookie);

        //            if (it.IsLastSection)
        //                break;

        //            it.ConsumeSection();
        //        }
        //    }
        //}

        public override IEnumerable<RequestHeaders.KeyValuePair> GetEnumerator() {
            foreach (var cookie in Storage) {
                yield return new RequestHeaders.KeyValuePair(Key, Serialize(cookie.Value));
            }
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