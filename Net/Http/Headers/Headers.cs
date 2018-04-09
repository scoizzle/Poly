using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Collections;

    public class GetStorage {
        public static KeyValueCollection<string>.CachedValue<DateTime> DateTime(KeyValueCollection<string> headers, string key) =>
            headers.GetCachedStorage<DateTime>(key, DateTimeExtensions.TryFromHttpTimeString, DateTimeExtensions.TryToHttpTimeString);

        public static KeyValueCollection<string>.CachedValue<long> Long(KeyValueCollection<string> headers, string key) =>
            headers.GetCachedStorage(
                key, 
                long.TryParse,
                (long value, out string text) => {
                    text = value.ToString();
                    return true;
                });
    }
}
