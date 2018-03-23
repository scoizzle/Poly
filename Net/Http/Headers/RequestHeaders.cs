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
            accept = GetStorage("accept");
            accept_charset = GetStorage("accept-charset");
            accept_encoding = GetStorage("accept-encoding");
            accept_language = GetStorage("accept-language");
            access_control_request_method = GetStorage("access-control-request-method");
            authorization = GetStorage("authorization");
            cache_control = GetStorage("cache-control");
            connection = GetStorage("connection");
            content_type = GetStorage("content-type");
            expect = GetStorage("expect");
            forwarded = GetStorage("forwarded");
            from = GetStorage("from");
            host = GetStorage("host");
            if_match = GetStorage("if-match");
            if_none_match = GetStorage("if-none-match");
            if_range = GetStorage("if-range");
            if_unmodified_since = GetStorage("if-unmodified-since");
            max_forwards = GetStorage("max-forwards");
            origin = GetStorage("origin");
            pragma = GetStorage("pragma");
            proxy_authorization = GetStorage("proxy-authorization");
            range = GetStorage("range");
            referer = GetStorage("referer");
            te = GetStorage("te");
            upgrade = GetStorage("upgrade");
            user_agent = GetStorage("user-agent");
            via = GetStorage("via");
            warning = GetStorage("warning");

            date = GetCachedStorage<DateTime>(
                "Date",
                HttpExtensions.TryFromHttpTimeString,
                HttpExtensions.TryToHttpTimeString
                );

            if_modified_since = GetCachedStorage<DateTime>(
                "If-Modified-Since",
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

            Cookie = new RequestCookieStorage(this);
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

        public RequestCookieStorage Cookie { get; private set; }

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