using System;

namespace Poly.Net.Http {
    using Collections;

    public class ResponseHeaders : KeyValueCollection<string> {
        CachedValue<DateTime> date;
        CachedValue<DateTime> last_modified;
        CachedValue<DateTime> expires;

        CachedValue<long> content_length;
        
        public ResponseHeaders() { 
            date = GetCachedStorage<DateTime>(
                "Date",
                DateTimeExtensions.TryFromHttpTimeString,
                DateTimeExtensions.TryToHttpTimeString
                );

            last_modified = GetCachedStorage<DateTime>(
                "Last-Modified",
                DateTimeExtensions.TryFromHttpTimeString,
                DateTimeExtensions.TryToHttpTimeString
                );

            expires = GetCachedStorage<DateTime>(
                "Expires",
                DateTimeExtensions.TryFromHttpTimeString,
                DateTimeExtensions.TryToHttpTimeString
                );

            content_length = GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long value, out string text) => {
                    text = value.ToString();
                    return true;
                });

            Cookies = new ResponseCookieStorage(this);
        }

        KeyValuePair access_control_allow_origin;
        public string AccessControlAllowOrigin {
            get => (access_control_allow_origin ?? (access_control_allow_origin = GetStorage("access_control_allow_origin"))).Value;
            set => (access_control_allow_origin ?? (access_control_allow_origin = GetStorage("access_control_allow_origin"))).Value = value;
        }

        KeyValuePair access_control_allow_credentials;
        public string AccessControlAllowCredentials {
            get => (access_control_allow_credentials ?? (access_control_allow_credentials = GetStorage("access_control_allow_credentials"))).Value;
            set => (access_control_allow_credentials ?? (access_control_allow_credentials = GetStorage("access_control_allow_credentials"))).Value = value;
        }

        KeyValuePair access_control_expose_headers;
        public string AccessControlExposeHeaders {
            get => (access_control_expose_headers ?? (access_control_expose_headers = GetStorage("access_control_expose_headers"))).Value;
            set => (access_control_expose_headers ?? (access_control_expose_headers = GetStorage("access_control_expose_headers"))).Value = value;
        }

        KeyValuePair access_control_max_age;
        public string AccessControlMaxAge {
            get => (access_control_max_age ?? (access_control_max_age = GetStorage("access_control_max_age"))).Value;
            set => (access_control_max_age ?? (access_control_max_age = GetStorage("access_control_max_age"))).Value = value;
        }

        KeyValuePair access_control_allow_methods;
        public string AccessControlAllowMethods {
            get => (access_control_allow_methods ?? (access_control_allow_methods = GetStorage("access_control_allow_methods"))).Value;
            set => (access_control_allow_methods ?? (access_control_allow_methods = GetStorage("access_control_allow_methods"))).Value = value;
        }

        KeyValuePair access_control_allow_headers;
        public string AccessControlAllowHeaders {
            get => (access_control_allow_headers ?? (access_control_allow_headers = GetStorage("access_control_allow_headers"))).Value;
            set => (access_control_allow_headers ?? (access_control_allow_headers = GetStorage("access_control_allow_headers"))).Value = value;
        }

        KeyValuePair accept_patch;
        public string AcceptPatch {
            get => (accept_patch ?? (accept_patch = GetStorage("accept_patch"))).Value;
            set => (accept_patch ?? (accept_patch = GetStorage("accept_patch"))).Value = value;
        }

        KeyValuePair accept_ranges;
        public string AcceptRanges {
            get => (accept_ranges ?? (accept_ranges = GetStorage("accept_ranges"))).Value;
            set => (accept_ranges ?? (accept_ranges = GetStorage("accept_ranges"))).Value = value;
        }

        KeyValuePair age;
        public string Age {
            get => (age ?? (age = GetStorage("age"))).Value;
            set => (age ?? (age = GetStorage("age"))).Value = value;
        }

        KeyValuePair allow;
        public string Allow {
            get => (allow ?? (allow = GetStorage("allow"))).Value;
            set => (allow ?? (allow = GetStorage("allow"))).Value = value;
        }

        KeyValuePair alt_svc;
        public string AltSvc {
            get => (alt_svc ?? (alt_svc = GetStorage("alt_svc"))).Value;
            set => (alt_svc ?? (alt_svc = GetStorage("alt_svc"))).Value = value;
        }

        KeyValuePair cache_control;
        public string CacheControl {
            get => (cache_control ?? (cache_control = GetStorage("cache_control"))).Value;
            set => (cache_control ?? (cache_control = GetStorage("cache_control"))).Value = value;
        }

        KeyValuePair connection;
        public string Connection {
            get => (connection ?? (connection = GetStorage("connection"))).Value;
            set => (connection ?? (connection = GetStorage("connection"))).Value = value;
        }

        KeyValuePair content_disposition;
        public string ContentDisposition {
            get => (content_disposition ?? (content_disposition = GetStorage("content_disposition"))).Value;
            set => (content_disposition ?? (content_disposition = GetStorage("content_disposition"))).Value = value;
        }

        KeyValuePair content_encoding;
        public string ContentEncoding {
            get => (content_encoding ?? (content_encoding = GetStorage("content_encoding"))).Value;
            set => (content_encoding ?? (content_encoding = GetStorage("content_encoding"))).Value = value;
        }

        KeyValuePair content_language;
        public string ContentLanguage {
            get => (content_language ?? (content_language = GetStorage("content_language"))).Value;
            set => (content_language ?? (content_language = GetStorage("content_language"))).Value = value;
        }

        public long? ContentLength {
            get => content_length?.Value;
            set => content_length.Value = value.HasValue ? value.Value : 0;
        }
        
        KeyValuePair content_location;
        public string ContentLocation {
            get => (content_location ?? (content_location = GetStorage("content_location"))).Value;
            set => (content_location ?? (content_location = GetStorage("content_location"))).Value = value;
        }

        KeyValuePair content_range;
        public string ContentRange {
            get => (content_range ?? (content_range = GetStorage("content_range"))).Value;
            set => (content_range ?? (content_range = GetStorage("content_range"))).Value = value;
        }

        KeyValuePair content_type;
        public string ContentType {
            get => (content_type ?? (content_type = GetStorage("content_type"))).Value;
            set => (content_type ?? (content_type = GetStorage("content_type"))).Value = value;
        }

        public ResponseCookieStorage Cookies { get; private set; }

        public DateTime Date {
            get => date.Value;
            set => date.Value = value;
        }

        KeyValuePair etag;
        public string ETag {
            get => (etag ?? (etag = GetStorage("etag"))).Value;
            set => (etag ?? (etag = GetStorage("etag"))).Value = value;
        }

        public DateTime Expires {
            get => expires.Value;
            set => expires.Value = value;
        }

        public DateTime LastModified {
            get => last_modified.Value;
            set => last_modified.Value = value;
        }

        KeyValuePair link;
        public string Link {
            get => (link ?? (link = GetStorage("link"))).Value;
            set => (link ?? (link = GetStorage("link"))).Value = value;
        }

        KeyValuePair location;
        public string Location {
            get => (location ?? (location = GetStorage("location"))).Value;
            set => (location ?? (location = GetStorage("location"))).Value = value;
        }

        KeyValuePair p3p;
        public string P3P {
            get => (p3p ?? (p3p = GetStorage("p3p"))).Value;
            set => (p3p ?? (p3p = GetStorage("p3p"))).Value = value;
        }

        KeyValuePair pragma;
        public string Pragma {
            get => (pragma ?? (pragma = GetStorage("pragma"))).Value;
            set => (pragma ?? (pragma = GetStorage("pragma"))).Value = value;
        }

        KeyValuePair proxy_authenticate;
        public string ProxyAuthenticate {
            get => (proxy_authenticate ?? (proxy_authenticate = GetStorage("proxy_authenticate"))).Value;
            set => (proxy_authenticate ?? (proxy_authenticate = GetStorage("proxy_authenticate"))).Value = value;
        }

        KeyValuePair public_key_pins;
        public string PublicKeyPins {
            get => (public_key_pins ?? (public_key_pins = GetStorage("public_key_pins"))).Value;
            set => (public_key_pins ?? (public_key_pins = GetStorage("public_key_pins"))).Value = value;
        }

        KeyValuePair retry_after;
        public string RetryAfter {
            get => (retry_after ?? (retry_after = GetStorage("retry_after"))).Value;
            set => (retry_after ?? (retry_after = GetStorage("retry_after"))).Value = value;
        }

        KeyValuePair server;
        public string Server {
            get => (server ?? (server = GetStorage("server"))).Value;
            set => (server ?? (server = GetStorage("server"))).Value = value;
        }

        KeyValuePair strict_transport_security;
        public string StrictTransportSecurity {
            get => (strict_transport_security ?? (strict_transport_security = GetStorage("strict_transport_security"))).Value;
            set => (strict_transport_security ?? (strict_transport_security = GetStorage("strict_transport_security"))).Value = value;
        }

        KeyValuePair trailer;
        public string Trailer {
            get => (trailer ?? (trailer = GetStorage("trailer"))).Value;
            set => (trailer ?? (trailer = GetStorage("trailer"))).Value = value;
        }

        KeyValuePair transfer_encoding;
        public string TransferEncoding {
            get => (transfer_encoding ?? (transfer_encoding = GetStorage("transfer_encoding"))).Value;
            set => (transfer_encoding ?? (transfer_encoding = GetStorage("transfer_encoding"))).Value = value;
        }

        KeyValuePair tk;
        public string Tk {
            get => (tk ?? (tk = GetStorage("tk"))).Value;
            set => (tk ?? (tk = GetStorage("tk"))).Value = value;
        }

        KeyValuePair upgrade;
        public string Upgrade {
            get => (upgrade ?? (upgrade = GetStorage("upgrade"))).Value;
            set => (upgrade ?? (upgrade = GetStorage("upgrade"))).Value = value;
        }

        KeyValuePair vary;
        public string Vary {
            get => (vary ?? (vary = GetStorage("vary"))).Value;
            set => (vary ?? (vary = GetStorage("vary"))).Value = value;
        }

        KeyValuePair via;
        public string Via {
            get => (via ?? (via = GetStorage("via"))).Value;
            set => (via ?? (via = GetStorage("via"))).Value = value;
        }

        KeyValuePair warning;
        public string Warning {
            get => (warning ?? (warning = GetStorage("warning"))).Value;
            set => (warning ?? (warning = GetStorage("warning"))).Value = value;
        }

        KeyValuePair www_authenticate;
        public string WWWAuthenticate {
            get => (www_authenticate ?? (www_authenticate = GetStorage("www_authenticate"))).Value;
            set => (www_authenticate ?? (www_authenticate = GetStorage("www_authenticate"))).Value = value;
        }
    }
}
