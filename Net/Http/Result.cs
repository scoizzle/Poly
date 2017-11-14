using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    public enum Result {
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

    public static class ResultExtensions {
        public static string GetString(this Result This) {
            return string.Concat(
                GetCode(This),
                ' ',
                GetPhrase(This)
                );
        }

        public static int GetCode(this Result This) {
            return ((int)(This));
        }

        public static string GetPhrase(this Result This) {
            switch (This) {
                case Result.Continue: return "Continue";
                case Result.SwitchingProtocols: return "Switching Protocols"; 

                case Result.Ok: return "Ok";
                case Result.Created: return "Created";
                case Result.Accepted: return "Accepted";
                case Result.PartialInfo: return "Partial Info";
                case Result.NoContent: return "No Content";
                case Result.ResetContent: return "Reset Content";
                case Result.PartialContent: return "Partial Content";

                case Result.MultipleChoices: return "Multiple Choices";
                case Result.MovedPermanently: return "Moved Permanently";
                case Result.Found: return "Found";
                case Result.SeeOther: return "See Other";
                case Result.NotModified: return "Not Modified";
                case Result.UseProxy: return "Use Proxy";
                case Result.TemporaryRedirect: return "Temporary Redirect";

                case Result.BadRequest: return "Bad Request";
                case Result.Unauthorized: return "Unauthorized";
                case Result.PaymentRequired: return "PaymentRequired";
                case Result.Forbidden: return "Forbidden";
                case Result.NotFound: return "Not Found";
                case Result.MethodNotAllowed: return "Method Not Allowed";
                case Result.NotAcceptable: return "Not Acceptable";
                case Result.ProxyAuthenticationRequired: return "Proxy Authentication Required";
                case Result.RequestTimeout: return "Request Timeout";
                case Result.Conflict: return "Conflict";
                case Result.Gone: return "Gone";
                case Result.LengthRequired: return "Length Required";
                case Result.PreconditionFailed: return "Precondition Failed";
                case Result.RequestEntityTooLarge: return "Request Entity Too Large";
                case Result.RequestUriTooLong: return "Request Uri Too Long";
                case Result.UnsupportedMediaType: return "Unsupported Media Type";
                case Result.RequestedRangeNotSatisfiable: return "Requested Range Not Satisfiable";
                case Result.ExpectationFailed: return "Expectation Failed";
                case Result.UnavailableForLegalReasons: return "Unavailable For Legal Reasons";

                case Result.InternalError: return "Internal Error";
                case Result.NotImplemented: return "Not Implemented";
                case Result.BadGateway: return "Bad Gateway";
                case Result.ServiceUnavailable: return "Service Unavailable";
                case Result.GatewayTimeout: return "Gateway Timeout";
                case Result.VersionNotSupported: return "Version Not Supported";
            }

            return "Undefined";
        }
    }
}
