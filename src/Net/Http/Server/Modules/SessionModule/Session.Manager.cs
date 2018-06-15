using System;
using System.Collections.Generic;

namespace Poly.Net.Http {
    public partial class SessionModule {
        static Dictionary<Guid, Session> ActiveSessions = new Dictionary<Guid, Session>();

        static Session GetSession(Guid identifier) {
            if (ActiveSessions.TryGetValue(identifier, out Session session))
                return session;

            return default;
        }

        static Session GetNewSession() {
            var identifier = Guid.NewGuid();
            var session = new Session(identifier);

            ActiveSessions.Add(identifier, session);
            return session;
        }
    }
}
