using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public partial class Event {
        public class Engine {
            public class Item {
                public Matcher Comparer;
                public List<Handler> Workers;

                public Item(string Fmt) {
                    Workers = new List<Handler>();
                    Comparer = new Matcher(Fmt);
                }
            }

            Dictionary<string, Item> Handlers;

            public Engine() {
                Handlers = new Dictionary<string, Item>();
            }

            public string[] Names {
                get {
                    return Handlers.Keys.ToArray();
                }
            }

            public void Register(string EventName, Handler Handler) {
                Item List;

                if (!Handlers.TryGetValue(EventName, out List)) {
                    Handlers.Add(EventName, List = new Item(EventName));
                }

                List.Workers.Add(Handler);
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

            public void Add(string Name, Handler Handler) {
                Register(Name, Handler);
            }

            public bool MatchAndInvoke(string Data, jsObject Args) {
                return MatchAndInvoke(Data, Args, false);
            }

            public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
                Item Work = null;
                var List = Handlers.ToArray();
                var Matches = default(jsObject);

                if (KeyIsWild) {
                    for (int i = 0; i < List.Length; i ++) {
                        var Item = List[i].Value;
                        Matches = Item.Comparer.Match(Data);

                        if (Matches != null) {
                            Work = Item;
                            break;
                        }
                    }
                }
                else {
                    var Match = new Matcher(Data);

                    for (int i = 0; i < List.Length; i ++) {
                        var Item = List[i].Key;
                        Matches = Match.Match(Item);

                        if (Matches != null) {
                            Work = List[i].Value;
                            break;
                        }
                    }
                }

                if (Work == null)
                    return false;

                if (Matches.Count == 0) {
                    Matches = Args;
                }
                else if (Args is jsComplex) {
                    Matches.CopyTo(Args);
                    Matches = Args;
                }
                else {
                    Args.CopyTo(Matches);
                }

                var Workers = Work.Workers.ToArray();
                for (int i = 0; i < Workers.Length; i ++){
                    Workers[i](Matches);
                }

                return true;
            }
        }
    }
}
