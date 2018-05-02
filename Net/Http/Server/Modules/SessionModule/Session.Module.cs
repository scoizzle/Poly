using System;

namespace Poly.Net.Http {
    public partial class SessionModule : HttpServer.Module {
        internal static object SessionItemKey = new object();

        internal SessionModule(HttpServer http_server) { }

        public string SessionCookieName { get; set; }

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            async context => {
                //var cookie = context.Request.Headers.Cookies[SessionCookieName];
                //if (cookie != null) {
                //    if (ActiveSessions.TryGetValue(Guid.Parse(cookie.Value), out Session session)) {
                //        context.Items[SessionItemKey] = session;
                //    }
                //}

                // Check for session cookie, check for existing session;
                // if it exists, aquire it's storage and add it to the items list;
                // otherwise create new session and set the response cookie

                // handoff to the next module to continue the execution chain
                await next(context);
            };
    }

    public static class SessionModuleExtensions {
        public static HttpServer UseSessions(this HttpServer server) {
            server.Modules.Add(new SessionModule(server));
            return server;
        }
    }

    public static class SessionContextExtensions {
        public static SessionModule.Session GetSession(this HttpServer.Context context) =>
            context.Items.TryGetValue(SessionModule.SessionItemKey, out object session) ? session as SessionModule.Session : default;        
    }
}
