using System;

namespace Poly.Net.Http {
    using Collections;

    public partial class HeaderStorage : KeyValueCollection<string> {
        private CachedValue<DateTime> date;
        private CachedValue<DateTime> last_modified;
        private CachedValue<DateTime> if_modified_since;
        private CachedValue<DateTime> expires;

        private KeyValuePair content_type;
        private CachedValue<long> content_length;

        public CookieStorage Cookies;

        public HeaderStorage() : base(StringExtensions.CompareIgnoreCase) {
            date = GetCachedStorage<DateTime>(
                "Date",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            last_modified = GetCachedStorage<DateTime>(
                "Last-Modified",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            if_modified_since = GetCachedStorage<DateTime>(
                "If-Modified-Since",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            expires = GetCachedStorage<DateTime>(
                "Expires",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            content_length = GetCachedStorage<long>(
                "Content-Length",
                long.TryParse,
                (long value, out string text) => {
                    text = value.ToString();
                    return true;
                });

            content_type = GetStorage("Content-Type");

            Cookies = new CookieStorage(this);
        }

        public string ContentType {
            get => content_type.value;
            set => content_type.Value = value;
        }

        public long? ContentLength {
            get => content_length?.Value;
            set => content_length.Value = value.HasValue ? value.Value : 0;
        }

        public DateTime Date {
            get => date.Value;
            set => date.Value = value;
        }

        public DateTime LastModified {
            get => last_modified.Value;
            set => last_modified.Value = value;
        }

        public DateTime IfModifiedSince {
            get => if_modified_since.Value;
            set => if_modified_since.Value = value;
        }

        public DateTime Expires {
            get => expires.Value;
            set => expires.Value = value;
        }
    }
}