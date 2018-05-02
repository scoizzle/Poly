using System;

namespace Poly.Net.Http {
    public class RequestHeaders : HeaderDictionary {
        Header accept;
        Header accept_charset;
        Header accept_encoding;
        Header accept_language;
        Header access_control_request_method;
        Header authorization;
        Header cache_control;
        Header connection;
        Header content_type;
        Header expect;
        Header forwarded;
        Header from;
        Header host;
        Header if_match;
        Header if_none_match;
        Header if_range;
        Header if_unmodified_since;
        Header max_forwards;
        Header origin;
        Header pragma;
        Header proxy_authorization;
        Header range;
        Header referer;
        Header te;
        Header upgrade;
        Header user_agent;
        Header via;
        Header warning;

        DateTimeHeader date;
        DateTimeHeader if_modified_since;

        LongHeader content_length;

        public RequestHeaders() {
            accept = Add("Accept");
            accept_charset = Add("Accept-Charset");
            accept_encoding = Add("Accept-Encoding");
            accept_language = Add("Accept-Language");
            access_control_request_method = Add("Access-Control-Request-Method");
            authorization = Add("Authorization");
            cache_control = Add("Cache-Control");
            connection = Add("Connection");
            content_type = Add("Content-Type");
            expect = Add("Expect");
            forwarded = Add("Forwarded");
            from = Add("From");
            host = Add("Host");
            if_match = Add("If-Match");
            if_none_match = Add("If-None-Match");
            if_range = Add("If-Range");
            if_unmodified_since = Add("If-Unmodified-Since");
            max_forwards = Add("Max-Forwards");
            origin = Add("Origin");
            pragma = Add("Pragma");
            proxy_authorization = Add("Proxy-Authorization");
            range = Add("Range");
            referer = Add("Referer");
            te = Add("TE");
            upgrade = Add("Upgrade");
            user_agent = Add("User-Agent");
            via = Add("Via");
            warning = Add("Warning");

            date = Add(new DateTimeHeader("Date"));
            if_modified_since = Add(new DateTimeHeader("If-Modified-Since"));
            
            content_length = Add(new LongHeader("Content-Length"));
            Cookies = Add(new CookieHeader());
        }

        public string Accept {
            get => accept.Value;
            set => accept.Value = value;
        }

        public string AcceptCharset {
            get => accept_charset.Value;
            set => accept_charset.Value = value;
        }

        public string AcceptEncoding {
            get => accept_encoding.Value;
            set => accept_encoding.Value = value;
        }

        public string AcceptLanguage {
            get => accept_language.Value;
            set => accept_language.Value = value;
        }

        public string AccessControlRequestMethod {
            get => access_control_request_method.Value;
            set => access_control_request_method.Value = value;
        }

        public string Authorization {
            get => authorization.Value;
            set => authorization.Value = value;
        }

        public string CacheControl {
            get => cache_control.Value;
            set => cache_control.Value = value;
        }

        public string Connection {
            get => connection.Value;
            set => connection.Value = value;
        }

        public long ContentLength {
            get => content_length.Value;
            set => content_length.Value = value;
        }

        public string ContentType {
            get => content_type.Value;
            set => content_type.Value = value;
        }

        public CookieHeader Cookies { get; private set; }

        public DateTime? Date {
            get => date.Value;
            set => date.Value = value;
        }

        public string Expect {
            get => expect.Value;
            set => expect.Value = value;
        }

        public string Forwarded {
            get => forwarded.Value;
            set => forwarded.Value = value;
        }

        public string From {
            get => from.Value;
            set => from.Value = value;
        }

        public string Host {
            get => host.Value;
            set => host.Value = value;
        }

        public string IfMatch {
            get => if_match.Value;
            set => if_match.Value = value;
        }

        public DateTime? IfModifiedSince {
            get => if_modified_since.Value;
            set => if_modified_since.Value = value;
        }

        public string IfNoneMatch {
            get => if_none_match.Value;
            set => if_none_match.Value = value;
        }

        public string IfRange {
            get => if_range.Value;
            set => if_range.Value = value;
        }

        public string IfUnmodifiedSince {
            get => if_unmodified_since.Value;
            set => if_unmodified_since.Value = value;
        }

        public string MaxForwards {
            get => max_forwards.Value;
            set => max_forwards.Value = value;
        }

        public string Origin {
            get => origin.Value;
            set => origin.Value = value;
        }

        public string Pragma {
            get => pragma.Value;
            set => pragma.Value = value;
        }

        public string ProxyAuthorization {
            get => proxy_authorization.Value;
            set => proxy_authorization.Value = value;
        }

        public string Range {
            get => range.Value;
            set => range.Value = value;
        }

        public string Referer {
            get => referer.Value;
            set => referer.Value = value;
        }

        public string TE {
            get => te.Value;
            set => te.Value = value;
        }

        public string Upgrade {
            get => upgrade.Value;
            set => upgrade.Value = value;
        }

        public string UserAgent {
            get => user_agent.Value;
            set => user_agent.Value = value;
        }

        public string Via {
            get => via.Value;
            set => via.Value = value;
        }

        public string Warning {
            get => warning.Value;
            set => warning.Value = value;
        }
    }
}