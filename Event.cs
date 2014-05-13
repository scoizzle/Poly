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

			return Func (Args);
		}

        public static object Invoke(Handler Func, params object[] ArgPairs) {
            if (Func == null)
                return null;

			return Func(new jsObject(ArgPairs));
        }

        public static object Invoke(Handler Func, jsObject Args, params object[] ArgPairs) {
            for (int i = 0; i < ArgPairs.Length / 2; i++) {
                Args[ArgPairs[i].ToString()] = ArgPairs[i + 1];
                i++;
            }

            return Func(Args);
        }

        public class Engine : jsObject<Handler> {
            public override void AssignValue<T>(string Key, T Value) {
                if (Value is Event.Handler) {
                    this.Add(Key, Value as Event.Handler);
                }
                else {
                    base.AssignValue<T>(Key, Value);
                }
            }

            public void Add(Handler Handler) {
                Register(Handler.Method.Name, Handler);
            }

            public new void Add(string Name, Handler Handler) {
                Register(Name, Handler);
            }

            public void Register(string EventName, Handler Handler) {
                base[EventName, Handler.Method.MetadataToken.ToString()] = Handler;
            }

            public void Unregister(string EventName, Handler Handler) {
                base[EventName, Handler.Method.MetadataToken.ToString()] = null;
            }

            public void Invoke(string EventName, jsObject Args) {
                var List = getObject(EventName);

                if (List != null) {
                    List.ForEach((K, V) => {
                        (V as Handler)(Args);
                    });
                }
            }

            public IEnumerable<object> Execute(string EventName, jsObject Args) {
                var List = getObject(EventName);

                for (int i = 0; i < List.Count; i++) {
                    yield return (List.ElementAt(i).Value as Handler)(Args);
                }
            }

            public bool MatchAndInvoke(string Data, jsObject Args) {
                return MatchAndInvoke(Data, Args, false);
            }

            public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
                var List = this.ToList();

                for (int i = 0; i < List.Count; i++) {
                    var Key = KeyIsWild ? Data : List[i].Key;
                    var Wild = KeyIsWild ? List[i].Key : Data;

                    var Matches = Key.Match(Wild);

                    if (Matches != null) {
                        if (Matches.Count == 0) {
                            Matches = Args;
                        }
                        else {
                            Args.CopyTo(Matches);
                        }

                        (List[i].Value as jsObject).ForEach<Handler>((K, H) => {
                            H(Matches);
                        });
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
