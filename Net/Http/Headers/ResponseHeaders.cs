using System;

namespace Poly.Net.Http {
    using Collections;

    public class ResponseHeaders : KeyValueCollection<string> {
        KeyValuePair access_control_allow_origin;
        KeyValuePair access_control_allow_credentials;
        KeyValuePair access_control_expose_headers;
        KeyValuePair access_control_max_age;
        KeyValuePair access_control_allow_methods;
        KeyValuePair access_control_allow_headers;
        KeyValuePair accept_patch;
        KeyValuePair accept_ranges;
        KeyValuePair age;
        KeyValuePair allow;
        KeyValuePair alt_svc;
        KeyValuePair cache_control;
        KeyValuePair connection;
        KeyValuePair content_disposition;
        KeyValuePair content_encoding;
        KeyValuePair content_language;
        KeyValuePair content_location;
        KeyValuePair content_range;
        KeyValuePair content_type;
        KeyValuePair etag;
        KeyValuePair link;
        KeyValuePair location;
        KeyValuePair p3p;
        KeyValuePair pragma;
        KeyValuePair proxy_authenticate;
        KeyValuePair public_key_pins;
        KeyValuePair retry_after;
        KeyValuePair server;
        KeyValuePair set_cookie;
        KeyValuePair strict_transport_security;
        KeyValuePair trailer;
        KeyValuePair transfer_encoding;
        KeyValuePair tk;
        KeyValuePair upgrade;
        KeyValuePair vary;
        KeyValuePair via;
        KeyValuePair warning;
        KeyValuePair www_authenticate;

        CachedValue<DateTime> date;
        CachedValue<DateTime> last_modified;
        CachedValue<DateTime> expires;

        CachedValue<long> content_length;

        public ResponseHeaders() {
            access_control_allow_origin = GetStorage("Access-Control-Allow-Origin");
            access_control_allow_credentials = GetStorage("Access-Control-Allow-Credentials");
            access_control_expose_headers = GetStorage("Access-Control-Expose-Headers");
            access_control_max_age = GetStorage("Access-Control-Max-Age");
            access_control_allow_methods = GetStorage("Access-Control-Allow-Methods");
            access_control_allow_headers = GetStorage("Access-Control-Allow-Headers");
            accept_patch = GetStorage("Accept-Patch");
            accept_ranges = GetStorage("Accept-Ranges");
            age = GetStorage("Age");
            allow = GetStorage("Allow");
            alt_svc = GetStorage("Alt-Svc");
            cache_control = GetStorage("Cache-Control");
            connection = GetStorage("Connection");
            content_disposition = GetStorage("Content-Disposition");
            content_encoding = GetStorage("Content-Encoding");
            content_language = GetStorage("Content-Language");
            content_location = GetStorage("Content-Location");
            content_range = GetStorage("Content-Range");
            content_type = GetStorage("Content-Type");
            etag = GetStorage("ETag");
            link = GetStorage("Link");
            location = GetStorage("Location");
            p3p = GetStorage("P3P");
            pragma = GetStorage("Pragma");
            proxy_authenticate = GetStorage("Proxy-Authenticate");
            public_key_pins = GetStorage("Public-Key-Pins");
            retry_after = GetStorage("Retry-After");
            server = GetStorage("Server");
            set_cookie = GetStorage("Set-Cookie");
            strict_transport_security = GetStorage("Strict-Transport-Security");
            trailer = GetStorage("Trailer");
            transfer_encoding = GetStorage("Transfer-Encoding");
            tk = GetStorage("Tk");
            upgrade = GetStorage("Upgrade");
            vary = GetStorage("Vary");
            via = GetStorage("Via");
            warning = GetStorage("Warning");
            www_authenticate = GetStorage("WWW-Authenticate");

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

            expires = GetCachedStorage<DateTime>(
                "Expires",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            content_length = GetCachedStorage(
                "Content-Length",
                long.TryParse,
                (long value, out string text) => {
                    text = value.ToString();
                    return true;
                });
        }


        public string AccessControlAllowOrigin {
            get => access_control_allow_origin.Value;
            set => access_control_allow_origin.Value = value;
        }

        public string AccessControlAllowCredentials {
            get => access_control_allow_credentials.Value;
            set => access_control_allow_credentials.Value = value;
        }

        public string AccessControlExposeHeaders {
            get => access_control_expose_headers.Value;
            set => access_control_expose_headers.Value = value;
        }

        public string AccessControlMaxAge {
            get => access_control_max_age.Value;
            set => access_control_max_age.Value = value;
        }

        public string AccessControlAllowMethods {
            get => access_control_allow_methods.Value;
            set => access_control_allow_methods.Value = value;
        }

        public string AccessControlAllowHeaders {
            get => access_control_allow_headers.Value;
            set => access_control_allow_headers.Value = value;
        }

        public string AcceptPatch {
            get => accept_patch.Value;
            set => accept_patch.Value = value;
        }

        public string AcceptRanges {
            get => accept_ranges.Value;
            set => accept_ranges.Value = value;
        }

        public string Age {
            get => age.Value;
            set => age.Value = value;
        }

        public string Allow {
            get => allow.Value;
            set => allow.Value = value;
        }

        public string AltSvc {
            get => alt_svc.Value;
            set => alt_svc.Value = value;
        }

        public string CacheControl {
            get => cache_control.Value;
            set => cache_control.Value = value;
        }

        public string Connection {
            get => connection.Value;
            set => connection.Value = value;
        }

        public string ContentDisposition {
            get => content_disposition.Value;
            set => content_disposition.Value = value;
        }

        public string ContentEncoding {
            get => content_encoding.Value;
            set => content_encoding.Value = value;
        }

        public string ContentLanguage {
            get => content_language.Value;
            set => content_language.Value = value;
        }

        public long? ContentLength {
            get => content_length?.Value;
            set => content_length.Value = value.HasValue ? value.Value : 0;
        }

        public string ContentLocation {
            get => content_location.Value;
            set => content_location.Value = value;
        }

        public string ContentRange {
            get => content_range.Value;
            set => content_range.Value = value;
        }

        public string ContentType {
            get => content_type.Value;
            set => content_type.Value = value;
        }

        public DateTime Date {
            get => date.Value;
            set => date.Value = value;
        }

        public string ETag {
            get => etag.Value;
            set => etag.Value = value;
        }

        public DateTime Expires {
            get => expires.Value;
            set => expires.Value = value;
        }

        public DateTime LastModified {
            get => last_modified.Value;
            set => last_modified.Value = value;
        }

        public string Link {
            get => link.Value;
            set => link.Value = value;
        }

        public string Location {
            get => location.Value;
            set => location.Value = value;
        }

        public string P3P {
            get => p3p.Value;
            set => p3p.Value = value;
        }

        public string Pragma {
            get => pragma.Value;
            set => pragma.Value = value;
        }

        public string ProxyAuthenticate {
            get => proxy_authenticate.Value;
            set => proxy_authenticate.Value = value;
        }

        public string PublicKeyPins {
            get => public_key_pins.Value;
            set => public_key_pins.Value = value;
        }

        public string RetryAfter {
            get => retry_after.Value;
            set => retry_after.Value = value;
        }

        public string Server {
            get => server.Value;
            set => server.Value = value;
        }

        public string SetCookie {
            get => set_cookie.Value;
            set => set_cookie.Value = value;
        }

        public string StrictTransportSecurity {
            get => strict_transport_security.Value;
            set => strict_transport_security.Value = value;
        }

        public string Trailer {
            get => trailer.Value;
            set => trailer.Value = value;
        }

        public string TransferEncoding {
            get => transfer_encoding.Value;
            set => transfer_encoding.Value = value;
        }

        public string Tk {
            get => tk.Value;
            set => tk.Value = value;
        }

        public string Upgrade {
            get => upgrade.Value;
            set => upgrade.Value = value;
        }

        public string Vary {
            get => vary.Value;
            set => vary.Value = value;
        }

        public string Via {
            get => via.Value;
            set => via.Value = value;
        }

        public string Warning {
            get => warning.Value;
            set => warning.Value = value;
        }

        public string WWWAuthenticate {
            get => www_authenticate.Value;
            set => www_authenticate.Value = value;
        }
    }
}
