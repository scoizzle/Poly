using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    using Data;

    public partial class Event {
        public class Engine<T> : KeyValueCollection<Engine<T>.Group> {
            public struct Group {
                public Matcher  Matcher;
                public T        Handler;
            }

            public void Add(string eventName, T handler) {
                Add(eventName, new Group {
                    Matcher = new Matcher(eventName),
                    Handler = handler
                });
            }

            public void Set(string eventName, T handler) {
                Set(eventName, new Group {
                    Matcher = new Matcher(eventName),
                    Handler = handler
                });
            }

            public T GetHandler(string Data) {
                T Handler;
                TryGetHandler(Data, out Handler);
                return Handler;
            }

            public T GetHandler(string Data, JSON Args) {
                T Handler;
                TryGetHandler(Data, Args, out Handler);
                return Handler;
            }

            public bool TryGetHandler(string Data, out T handler) {
                if (!string.IsNullOrEmpty(Data)) {
                    var found = 
                        TryFind(
                            out KeyValuePair item, 
                            (key, group) => {
                                return group.Matcher.Compare(Data);
                        });

                    if (found) {
                        handler = item.Value.Handler;
                        return true;
                    }
                }

                handler = default(T);
                return false;
            }

            public bool TryGetHandler(string Data, JSON Args, out T handler) {
                if (!string.IsNullOrEmpty(Data)) {
                    var found = 
                        TryFind(
                            out KeyValuePair item, 
                            (key, group) => {
                                return group.Matcher.Extract(Data, Args.Set);
                        });

                    if (found) {
                        handler = item.Value.Handler;
                        return true;
                    }
                }

                handler = default(T);
                return false;
            }
        }
    }
}
