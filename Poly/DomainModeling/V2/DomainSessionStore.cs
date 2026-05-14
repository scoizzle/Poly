using System.Collections.Concurrent;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Thread-safe registry of named <see cref="DomainSession"/> instances, supporting
/// create-or-retrieve semantics for UI, API, and MCP session lifecycles.
/// </summary>
public sealed class DomainSessionStore {
    private readonly ConcurrentDictionary<string, DomainSession> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _createLock = new();

    /// <summary>
    /// Creates a new session with canonical built-in types bootstrapped, seeds revision 0, and
    /// returns both the session ID and the created <see cref="DomainSession"/>.
    /// If <paramref name="preferredSessionId"/> is supplied and the ID is already in use the
    /// existing session is returned unchanged.
    /// </summary>
    public (string SessionId, DomainSession Session) Create(string domainName, string? preferredSessionId = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainName);

        lock (_createLock) {
            var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? Guid.NewGuid().ToString("N")
                : preferredSessionId;

            if (_sessions.TryGetValue(sessionId, out var existing)) {
                return (sessionId, existing);
            }

            var domain = new Domain(domainName);
            var analyzer = new DomainModelAnalyzer();
            var bootstrap = domain.CreateMutation(analyzer);
            CanonicalBuiltInTypeCatalog.AddToMutation(bootstrap);
            var initialAnalysis = bootstrap.Apply(preMutationAnalysis: null);

            var session = new DomainSession(domain, analyzer, initialAnalysis, initialRevision: 0);
            _sessions[sessionId] = session;
            return (sessionId, session);
        }
    }

    /// <summary>
    /// Tries to retrieve an existing session by ID.
    /// </summary>
    public bool TryGet(string sessionId, [NotNullWhen(true)] out DomainSession? session) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            session = null;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out session);
    }

    /// <summary>
    /// Removes a session from the store. Returns <see langword="true"/> if the session was found and removed.
    /// </summary>
    public bool Remove(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>Returns the IDs of all currently active sessions, ordered lexicographically.</summary>
    public IReadOnlyCollection<string> ListSessionIds() =>
        _sessions.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
}
