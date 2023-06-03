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
        public static string GetString(this Status status) => status switch
        {
            Status.Continue => "100 Continue",
            Status.SwitchingProtocols => "101 Switching Protocols",
            Status.Ok => "200 Ok",
            Status.Created => "201 Created",
            Status.Accepted => "202 Accepted",
            Status.PartialInfo => "203 Partial Info",
            Status.NoContent => "204 No Content",
            Status.ResetContent => "205 Reset Content",
            Status.PartialContent => "206 Partial Content",
            Status.MultipleChoices => "300 Multiple Choices",
            Status.MovedPermanently => "301 Moved Permanently",
            Status.Found => "302 Found",
            Status.SeeOther => "303 See Other",
            Status.NotModified => "304 Not Modified",
            Status.UseProxy => "305 Use Proxy",
            Status.SwitchProxy => "306 Switch Proxy",
            Status.TemporaryRedirect => "307 Temporary Redirect",
            Status.BadRequest => "400 Bad Request",
            Status.Unauthorized => "401 Unauthorized",
            Status.PaymentRequired => "402 Payment Required",
            Status.Forbidden => "403 Forbidden",
            Status.NotFound => "404 Not Found",
            Status.MethodNotAllowed => "405 Method Not Allowed",
            Status.NotAcceptable => "406 Not Acceptable",
            Status.ProxyAuthenticationRequired => "407 Proxy Authentication Required",
            Status.RequestTimeout => "408 Request Timeout",
            Status.Conflict => "409 Conflict",
            Status.Gone => "410 Gone",
            Status.LengthRequired => "411 Length Required",
            Status.PreconditionFailed => "412 Precondition Failed",
            Status.RequestEntityTooLarge => "415 Request Entity Too Large",
            Status.RequestUriTooLong => "414 Request Uri Too Long",
            Status.UnsupportedMediaType => "415 Unsupported Media Type",
            Status.RequestedRangeNotSatisfiable => "416 Requested Range Not Satisfiable",
            Status.ExpectationFailed => "417 Expectation Failed",
            Status.UnavailableForLegalReasons => "451 Unavailable For Legal Reasons",
            Status.InternalError => "500 Internal Error",
            Status.NotImplemented => "501 Not Implemented",
            Status.BadGateway => "502 Bad Gateway",
            Status.ServiceUnavailable => "503 Service Unavailable",
            Status.GatewayTimeout => "504 Gateway Timeout",
            Status.VersionNotSupported => "505 Version Not Supported",
            _ => null,
        };
    }
}