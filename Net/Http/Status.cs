using System.Text;

namespace Poly.Net.Http {

    public enum Status {
        Invalid,

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
        TemporaryRedirect = 307,

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
        public static StringBuilder AppendStatus(this StringBuilder builder, Status status) =>
            builder.Append(ResultExtensions.GetCode(status)).Append(' ').Append(ResultExtensions.GetPhrase(status));
    }

    public static class ResultExtensions {

        public static string GetString(this Status This) {
            return string.Concat(
                GetCode(This),
                ' ',
                GetPhrase(This)
                );
        }

        public static int GetCode(this Status This) {
            return ((int)(This));
        }

        public static string GetPhrase(this Status This) {
            switch (This) {
                case Status.Continue: return "Continue";
                case Status.SwitchingProtocols: return "Switching Protocols";

                case Status.Ok: return "Ok";
                case Status.Created: return "Created";
                case Status.Accepted: return "Accepted";
                case Status.PartialInfo: return "Partial Info";
                case Status.NoContent: return "No Content";
                case Status.ResetContent: return "Reset Content";
                case Status.PartialContent: return "Partial Content";

                case Status.MultipleChoices: return "Multiple Choices";
                case Status.MovedPermanently: return "Moved Permanently";
                case Status.Found: return "Found";
                case Status.SeeOther: return "See Other";
                case Status.NotModified: return "Not Modified";
                case Status.UseProxy: return "Use Proxy";
                case Status.TemporaryRedirect: return "Temporary Redirect";

                case Status.BadRequest: return "Bad Request";
                case Status.Unauthorized: return "Unauthorized";
                case Status.PaymentRequired: return "PaymentRequired";
                case Status.Forbidden: return "Forbidden";
                case Status.NotFound: return "Not Found";
                case Status.MethodNotAllowed: return "Method Not Allowed";
                case Status.NotAcceptable: return "Not Acceptable";
                case Status.ProxyAuthenticationRequired: return "Proxy Authentication Required";
                case Status.RequestTimeout: return "Request Timeout";
                case Status.Conflict: return "Conflict";
                case Status.Gone: return "Gone";
                case Status.LengthRequired: return "Length Required";
                case Status.PreconditionFailed: return "Precondition Failed";
                case Status.RequestEntityTooLarge: return "Request Entity Too Large";
                case Status.RequestUriTooLong: return "Request Uri Too Long";
                case Status.UnsupportedMediaType: return "Unsupported Media Type";
                case Status.RequestedRangeNotSatisfiable: return "Requested Range Not Satisfiable";
                case Status.ExpectationFailed: return "Expectation Failed";
                case Status.UnavailableForLegalReasons: return "Unavailable For Legal Reasons";

                case Status.InternalError: return "Internal Error";
                case Status.NotImplemented: return "Not Implemented";
                case Status.BadGateway: return "Bad Gateway";
                case Status.ServiceUnavailable: return "Service Unavailable";
                case Status.GatewayTimeout: return "Gateway Timeout";
                case Status.VersionNotSupported: return "Version Not Supported";
            }

            return "Undefined";
        }
    }
}