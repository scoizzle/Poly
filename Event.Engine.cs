using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                jsObject Matches = null;
                Item Workers = null;

                Matcher Match = new Matcher(Data);

                foreach (var Pair in Handlers) {
                    if ((Matches = Data.Match(Pair.Key)) != null) {
                        Workers = Pair.Value;
                        break;
                    }
                }

                if (Workers != null) {
                    Execute(Workers, Args, Matches);
                    return true;
                }

                return false;
            }

            public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
				if (string.IsNullOrEmpty (Data))
					return false;
				
                if (KeyIsWild) {
                    jsObject Matches = null;
                    Item Workers = null;

                    foreach (var Pair in Handlers) {
                        if ((Matches = Pair.Value.Comparer.Match(Data)) != null) {
                            Workers = Pair.Value;
                            break;
                        }
                    }

                    if (Workers != null) {
                        Execute(Workers, Args, Matches);
                        return true;
                    }

                    return false;
                }
                
                return MatchAndInvoke(Data, Args);
            }

            private void Execute(Item Item, jsObject Args, jsObject Matches) {
                if (Matches.Count == 0) {
                    Matches = Args;
                }
                else {
                    Args.CopyTo(Matches);
                }

                foreach (var Handler in Item.Workers) {
                    Handler(Matches);
                }
            }
        }
    }
}
