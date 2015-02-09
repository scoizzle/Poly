using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly {
    public partial class Event {
        public delegate object Handler(jsObject Args);

        public static object Invoke(Handler Func, jsObject Args) {
            if (Func == null)
                return null;

            return Func(Args);
        }

        public static object Invoke(Handler Func, jsObject Args, params object[] ArgPairs) {
            if (Func == null)
                return null;

            for (int i = 0; i < ArgPairs.Length / 2; i++) {
                Args[ArgPairs[i].ToString()] = ArgPairs[i + 1];
                i++;
            }

            return Func(Args);
        }

        public class Engine : Dictionary<string, List<Handler>> {
            public void Register(string EventName, Handler Handler) {
                if (!ContainsKey(EventName)) {
                    Add(EventName, new List<Handler>());
                }

                this[EventName].Add(Handler);
            }

            public void Register(string EventName, Handler Handler, Script.Types.ClassInstance This) {
                if (This == null) {
                    Register(EventName, Handler);
                    return;
                }

                Register(EventName, (Context) => { 
                    Context["this"] = This; 
                    return Handler(Context); 
                });
            }

            public void Add(Handler Handler) {
                Register(Handler.Method.Name, Handler);
            }

            public new void Add(string Name, Handler Handler) {
                Register(Name, Handler);
            }

            public bool MatchAndInvoke(string Data, jsObject Args) {
                return MatchAndInvoke(Data, Args, false);
            }

            public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
                foreach (var Pair in this) {
                    var Key = KeyIsWild ? Data : Pair.Key;
                    var Wild = KeyIsWild ? Pair.Key : Data;
                    var Matches = default(jsObject);

                    if ((Matches = Key.Match(Wild)) != null) {
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


                        foreach (var Event in Pair.Value) {
                            Event(Matches);
                        }

                        return true;
                    }
                }
            
                return false;
            }
        }
    }
}
