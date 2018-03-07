using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

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
