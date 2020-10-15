using System.Text;

namespace Poly.Net.Http {

    public enum Status {
        // Informational
        Continue = 100,

        SwitchingProtocols,

        // Success
        Ok = 200,

        Created,
        Accepted,
        PartialInfo,
        NoContent,
        ResetContent,
        PartialContent,

        // Redirects
        MultipleChoices = 300,

        MovedPermanently,
        Found,
        SeeOther,
        NotModified,
        UseProxy,
        SwitchProxy,
        TemporaryRedirect,

        // Client Error
        BadRequest = 400,

        Unauthorized,
        PaymentRequired,
        Forbidden,
        NotFound,
        MethodNotAllowed,
        NotAcceptable,
        ProxyAuthenticationRequired,
        RequestTimeout,
        Conflict,
        Gone,
        LengthRequired,
        PreconditionFailed,
        RequestEntityTooLarge,
        RequestUriTooLong,
        UnsupportedMediaType,
        RequestedRangeNotSatisfiable,
        ExpectationFailed,
        UnavailableForLegalReasons = 451,

        // Server Error
        InternalError = 500,

        NotImplemented,
        BadGateway,
        ServiceUnavailable,
        GatewayTimeout,
        VersionNotSupported
    }

    public static class StringBuilderExtension {
        public static StringBuilder Append(this StringBuilder builder, Status status)
            => builder.Append(status.GetString());
    }

    public static class ResultExtensions {
        public static string GetString(this Status This) {
            switch (This) {
                case Status.Continue: return "100 Continue";
                case Status.SwitchingProtocols: return "101 Switching Protocols";

                case Status.Ok: return "200 Ok";
                case Status.Created: return "201 Created";
                case Status.Accepted: return "202 Accepted";
                case Status.PartialInfo: return "203 Partial Info";
                case Status.NoContent: return "204 No Content";
                case Status.ResetContent: return "205 Reset Content";
                case Status.PartialContent: return "206 Partial Content";

                case Status.MultipleChoices: return "300 Multiple Choices";
                case Status.MovedPermanently: return "301 Moved Permanently";
                case Status.Found: return "302 Found";
                case Status.SeeOther: return "303 See Other";
                case Status.NotModified: return "304 Not Modified";
                case Status.UseProxy: return "305 Use Proxy";
                case Status.SwitchProxy: return "306 Switch Proxy";
                case Status.TemporaryRedirect: return "307 Temporary Redirect";

                case Status.BadRequest: return "400 Bad Request";
                case Status.Unauthorized: return "401 Unauthorized";
                case Status.PaymentRequired: return "402 Payment Required";
                case Status.Forbidden: return "403 Forbidden";
                case Status.NotFound: return "404 Not Found";
                case Status.MethodNotAllowed: return "405 Method Not Allowed";
                case Status.NotAcceptable: return "406 Not Acceptable";
                case Status.ProxyAuthenticationRequired: return "407 Proxy Authentication Required";
                case Status.RequestTimeout: return "408 Request Timeout";
                case Status.Conflict: return "409 Conflict";
                case Status.Gone: return "410 Gone";
                case Status.LengthRequired: return "411 Length Required";
                case Status.PreconditionFailed: return "412 Precondition Failed";
                case Status.RequestEntityTooLarge: return "415 Request Entity Too Large";
                case Status.RequestUriTooLong: return "414 Request Uri Too Long";
                case Status.UnsupportedMediaType: return "415 Unsupported Media Type";
                case Status.RequestedRangeNotSatisfiable: return "416 Requested Range Not Satisfiable";
                case Status.ExpectationFailed: return "417 Expectation Failed";

                case Status.UnavailableForLegalReasons: return "451 Unavailable For Legal Reasons";

                case Status.InternalError: return "500 Internal Error";
                case Status.NotImplemented: return "501 Not Implemented";
                case Status.BadGateway: return "502 Bad Gateway";
                case Status.ServiceUnavailable: return "503 Service Unavailable";
                case Status.GatewayTimeout: return "504 Gateway Timeout";
                case Status.VersionNotSupported: return "505 Version Not Supported";
            }

            return null;
        }
    }
}