using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Net.Http {
    public class Session : jsObject {
        public Server Server = null;

        public string Id { get; private set; }

        public Session(Request Req) {
            this.Id = CreateId(Req);
        }

        public static string CreateId(Request Req) {
            if (Req.Client == null || !Req.Client.Connected)
                return string.Empty;

            return Req.Client.Socket.RemoteEndPoint.ToString().MD5().SHA1();
        }

        public static Session CreateSession(Server Serv, Request Req, Host Info) {
            var S = new Session(Req);
            Serv.Sessions.Set(S.Id, S);
            Req.SetCookie(Info.SessionCookieName, S.Id, 0, Info.SessionPath, Info.SessionDomain);
            return S;
        }

        public static Session GetSession(Server Serv, Request Req, Host Info) {
            if (Req.Cookies.ContainsKey(Info.SessionCookieName)) {
                var Id = Req.Cookies.Get<string>(Info.SessionCookieName);

                if (!string.IsNullOrEmpty(Id) && Serv.Sessions.ContainsKey(Id)) {
                    return Serv.Sessions.Get<Session>(Id);
                }
            }
            return CreateSession(Serv, Req, Info);
        }
    }
}
