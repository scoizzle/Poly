using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    using Data;

    public partial class Event {
        public class Engine {
			ManagedArray<KeyValuePair<Matcher, ManagedArray<Handler>>> Handlers;

            public Engine() {
                Handlers = new ManagedArray<KeyValuePair<Matcher, ManagedArray<Handler>>>();
            }

            public void Add(string Name, Handler Handler) {
                Register(Name, Handler);
            }

            public void Register(string EventName, Handler Handler) {
                var Pair = Handlers.Where(p => string.Compare(p.Key.Format, EventName, StringComparison.Ordinal) == 0).FirstOrDefault();
                
                if (Pair.Value == null) {
                    Handlers.Add(
                        new KeyValuePair<Matcher, ManagedArray<Handler>>(
                            new Matcher(EventName), 
                            new ManagedArray<Handler>() {
                                Handler
                            }
                        )
                    );
                }
                else Pair.Value.Add(Handler);
            }

            public void Register(string EventName, Handler Handler, object This) {
                if (This == null) {
                    Register(EventName, Handler);
                    return;
                }

                Register(EventName, (Context) => {
                    Context["this"] = This;
                    return Handler(Context);
                });
            }

            public void Register(string EventName, Handler Handler, string Name, object This) {
                if (string.IsNullOrEmpty(Name) || This == null)
                    return;

                Register(EventName, (Context) => {
                    Context[Name] = This;
                    return Handler(Context);
                });
            }

            public bool MatchAndInvoke(string Data, jsObject Args) {
                var Len = Handlers.Count;
                for (int i = 0; i < Len; i++) {
                    var I = Handlers.Elements[i];
                    var R = I.Key.Match(Data);

                    if (R == null)
                        continue;
                    else
                    if (R.Count == 0)
                        R = Args;
                    else
                        Args.CopyTo(R);

                    Execute(I.Value, R);
                    return true;
                }
                return false;
            }

            private void Execute(ManagedArray<Handler> Workers, jsObject Args) {
                var WorkersLen = Workers.Count;
                for (int i = 0; i < WorkersLen; i++)
                    Workers.Elements[i](Args);
            }
        }
    }
}
