using System;

namespace Poly.Net.Http {
    using Collections;

    public class ResponseHeaders : HeaderDictionary {
        Header access_control_allow_origin;
        Header access_control_allow_credentials;
        Header access_control_expose_headers;
        Header access_control_max_age;
        Header access_control_allow_methods;
        Header access_control_allow_headers;
        Header accept_patch;
        Header accept_ranges;
        Header age;
        Header allow;
        Header alt_svc;
        Header cache_control;
        Header connection;
        Header content_disposition;
        Header content_encoding;
        Header content_language;
        Header content_range;
        Header content_type;
        Header etag;
        Header link;
        Header location;
        Header p3p;
        Header pragma;
        Header proxy_authenticate;
        Header public_key_pins;
        Header retry_after;
        Header server;
        Header strict_transport_security;
        Header trailer;
        Header transfer_encoding;
        Header tk;
        Header upgrade;
        Header vary;
        Header via;
        Header warning;
        Header www_authenticate;

        DateTimeHeader date;
        DateTimeHeader expires;
        DateTimeHeader last_modified;

        LongHeader content_length;
        
        public ResponseHeaders() { 
            access_control_allow_origin = Add("Access-Control-Allow-Origin");
            access_control_allow_credentials = Add("Access-Control-Allow-Credentials");
            access_control_expose_headers = Add("Access-Control-Expose-Headers");
            access_control_max_age = Add("Access-Control-Max-Age");
            access_control_allow_methods = Add("Access-Control-Allow-Methods");
            access_control_allow_headers = Add("Access-Control-Allow-Headers");
            accept_patch = Add("Accept-Patch");
            accept_ranges = Add("Accept-Ranges");
            age = Add("Age");
            allow = Add("Allow");
            alt_svc = Add("Alt-Svc");
            cache_control = Add("Cache-Control");
            connection = Add("Connection");
            content_disposition = Add("Content-Disposition");
            content_encoding = Add("Content-Encoding");
            content_language = Add("Content-Language");
            content_range = Add("Content-Range");
            content_type = Add("Content-Type");
            etag = Add("Etag");
            link = Add("Link");
            location = Add("Location");
            p3p = Add("P3P");
            pragma = Add("Pragma");
            proxy_authenticate = Add("Proxy-Authenticate");
            public_key_pins = Add("Public-Key-Pins");
            retry_after = Add("Retry-After");
            server = Add("Server");
            strict_transport_security = Add("Strict-Transport-Security");
            trailer = Add("Trailer");
            transfer_encoding = Add("Transfer-Encoding");
            tk = Add("Tk");
            upgrade = Add("Upgrade");
            vary = Add("Vary");
            via = Add("Via");
            warning = Add("Warning");
            www_authenticate = Add("WWW-Authenticate");
            
            date = Add(new DateTimeHeader("Date"));
            expires = Add(new DateTimeHeader("Expires"));
            last_modified = Add(new DateTimeHeader("Last-Modified"));

            content_length = Add(new LongHeader("Content-Length"));
            Cookies = Add(new SetCookieHeader());
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

        public long ContentLength {
            get => content_length.Value;
            set => content_length.Value = value;
        }

        public string ContentRange {
            get => content_range.Value;
            set => content_range.Value = value;
        }

        public string ContentType {
            get => content_type.Value;
            set => content_type.Value = value;
        }

        public SetCookieHeader Cookies { get; private set; }

        public DateTime? Date {
            get => date.Value;
            set => date.Value = value;
        }

        public string Etag {
            get => etag.Value;
            set => etag.Value = value;
        }

        public DateTime? Expires {
            get => expires.Value;
            set => expires.Value = value;
        }
        
        public DateTime? LastModified {
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
