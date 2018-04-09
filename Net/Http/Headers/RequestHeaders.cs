using System;

namespace Poly.Net.Http {
    using Collections;

    public class RequestHeaders : KeyValueCollection<string> {
        KeyValuePair accept;
        KeyValuePair accept_charset;
        KeyValuePair accept_encoding;
        KeyValuePair accept_language;
        KeyValuePair access_control_request_method;
        KeyValuePair authorization;
        KeyValuePair cache_control;
        KeyValuePair connection;
        KeyValuePair content_type;
        KeyValuePair expect;
        KeyValuePair forwarded;
        KeyValuePair from;
        KeyValuePair host;
        KeyValuePair if_match;
        KeyValuePair if_none_match;
        KeyValuePair if_range;
        KeyValuePair if_unmodified_since;
        KeyValuePair max_forwards;
        KeyValuePair origin;
        KeyValuePair pragma;
        KeyValuePair proxy_authorization;
        KeyValuePair range;
        KeyValuePair referer;
        KeyValuePair te;
        KeyValuePair upgrade;
        KeyValuePair user_agent;
        KeyValuePair via;
        KeyValuePair warning;

        CachedValue<DateTime> date;
        CachedValue<DateTime> if_modified_since;

        CachedValue<long> content_length;

        public RequestHeaders() {
            accept = GetStorage("Accept");
            accept_charset = GetStorage("Accept-Charset");
            accept_encoding = GetStorage("Accept-Encoding");
            accept_language = GetStorage("Accept-Language");
            access_control_request_method = GetStorage("Access-Control-Request-Method");
            authorization = GetStorage("Authorization");
            cache_control = GetStorage("Cache-Control");
            connection = GetStorage("Connection");
            content_type = GetStorage("Content-Type");
            expect = GetStorage("Expect");
            forwarded = GetStorage("Forwarded");
            from = GetStorage("From");
            host = GetStorage("Host");
            if_match = GetStorage("If-Match");
            if_none_match = GetStorage("If-None-Match");
            if_range = GetStorage("If-Range");
            if_unmodified_since = GetStorage("If-Unmodified-Since");
            max_forwards = GetStorage("Max-Forwards");
            origin = GetStorage("Origin");
            pragma = GetStorage("Pragma");
            proxy_authorization = GetStorage("Proxy-Authorization");
            range = GetStorage("Range");
            referer = GetStorage("Referer");
            te = GetStorage("TE");
            upgrade = GetStorage("Upgrade");
            user_agent = GetStorage("User-Agent");
            via = GetStorage("Via");
            warning = GetStorage("Warning");

            date = Http.GetStorage.DateTime(this, "date");

            if_modified_since = Http.GetStorage.DateTime(this, "If-Modified-Since");

            content_length = Http.GetStorage.Long(this, "Content-Length");

            Cookies = new RequestCookieStorage(this);
        }

        public string Accept {
            get => accept.value;
            set => accept.value = value;
        }

        public string AcceptCharset {
            get => accept_charset.value;
            set => accept_charset.value = value;
        }

        public string AcceptEncoding {
            get => accept_encoding.value;
            set => accept_encoding.value = value;
        }

        public string AcceptLanguage {
            get => accept_language.value;
            set => accept_language.value = value;
        }

        public string AccessControlRequestMethod {
            get => access_control_request_method.value;
            set => access_control_request_method.value = value;
        }

        public string Authorization {
            get => authorization.value;
            set => authorization.value = value;
        }

        public string CacheControl {
            get => cache_control.value;
            set => cache_control.value = value;
        }

        public string Connection {
            get => connection.value;
            set => connection.value = value;
        }

        public long? ContentLength {
            get => content_length?.Value;
            set => content_length.Value = value.HasValue ? value.Value : 0;
        }

        public string ContentType {
            get => content_type.value;
            set => content_type.value = value;
        }

        public RequestCookieStorage Cookies { get; private set; }

        public DateTime Date {
            get => date.Value;
            set => date.Value = value;
        }

        public string Expect {
            get => expect.value;
            set => expect.value = value;
        }

        public string Forwarded {
            get => forwarded.value;
            set => forwarded.value = value;
        }

        public string From {
            get => from.value;
            set => from.value = value;
        }

        public string Host {
            get => host.value;
            set => host.value = value;
        }

        public string IfMatch {
            get => if_match.value;
            set => if_match.value = value;
        }

        public DateTime IfModifiedSince {
            get => if_modified_since.Value;
            set => if_modified_since.Value = value;
        }

        public string IfNoneMatch {
            get => if_none_match.value;
            set => if_none_match.value = value;
        }

        public string IfRange {
            get => if_range.value;
            set => if_range.value = value;
        }

        public string IfUnmodifiedSince {
            get => if_unmodified_since.value;
            set => if_unmodified_since.value = value;
        }

        public string MaxForwards {
            get => max_forwards.value;
            set => max_forwards.value = value;
        }

        public string Origin {
            get => origin.value;
            set => origin.value = value;
        }

        public string Pragma {
            get => pragma.value;
            set => pragma.value = value;
        }

        public string ProxyAuthorization {
            get => proxy_authorization.value;
            set => proxy_authorization.value = value;
        }

        public string Range {
            get => range.value;
            set => range.value = value;
        }

        public string Referer {
            get => referer.value;
            set => referer.value = value;
        }

        public string TE {
            get => te.value;
            set => te.value = value;
        }

        public string Upgrade {
            get => upgrade.value;
            set => upgrade.value = value;
        }

        public string UserAgent {
            get => user_agent.value;
            set => user_agent.value = value;
        }

        public string Via {
            get => via.value;
            set => via.value = value;
        }

        public string Warning {
            get => warning.value;
            set => warning.value = value;
        }
    }
}