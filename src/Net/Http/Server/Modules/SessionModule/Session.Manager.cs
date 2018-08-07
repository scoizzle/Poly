using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        static void PruneInactiveSessions() {
            var expired = ActiveSessions.Where(pair => (DateTime.UtcNow - pair.Value.LastAccessTime).TotalMinutes > 1);

            foreach (var to_remove in expired)
                ActiveSessions.Remove(to_remove.Key);

            Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => PruneInactiveSessions());
        }
    }
}
