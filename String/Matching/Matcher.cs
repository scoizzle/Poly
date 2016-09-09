using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    public partial class Matcher {
        Block[] Handlers;

        public string Format { get; private set; }

        public Matcher(string Fmt) {
            Format = Fmt;

            try {
                Handlers = Parse(Fmt);
            } catch (Exception Error) {
                App.Log.Error(Error);
            }
        }

        public bool Compare(string Data) {
            if (Handlers == null)
                return false;

            var Context = new Context(Data, new jsObject()) {
                BlockCount = Handlers == null ?
                    0 : Handlers.Length
            };

            return Match(Context);
        }

		public jsObject Match(string Data) {
            return Match(Data, 0, new jsObject());
        }        

        public jsObject Match(string Data, int Index) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, new jsObject()) {
                Index = Index,
				BlockCount = Handlers == null ?
					0 : Handlers.Length
            };

            if (Match(Context)) {
                return Context.Storage;
            }

            return null;
        }

        public jsObject Match(string Data, jsObject Storage) {
            return Match(Data, 0, Storage);
        }

        public jsObject Match(string Data, jsObject Storage, bool KeyValueExtract) {
            return Match(Data, 0, Storage, KeyValueExtract);
        }

        public jsObject Match(string Data, int Index, jsObject Storage) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, Storage) {
				Index = Index,
				BlockCount = Handlers == null ?
					0 : Handlers.Length
            };

            if (Match(Context)) {
                return Storage;
            }

            return null;
        }

        public jsObject Match(string Data, int Index, jsObject Storage, bool KeyValueExtract) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, KeyValueExtract ? new jsObject() : Storage) {
                Index = Index,
                BlockCount = Handlers == null ?
                    0 : Handlers.Length
            };

            if (Match(Context)) {
                Index = Context.Index;

                if (KeyValueExtract) {
                    Storage.Set(Context.Storage.Get<string>("Key"), Context.Storage.Get("Value"));
                }

                return Storage;
            }

            return null;
        }

        public jsObject MatchAll(string Data) {
            int Index = 0;

            return MatchAll(Data, ref Index, new jsObject());
        }

        public jsObject MatchAll(string Data, bool SingleObject) {
            int Index = 0;

            return MatchAll(Data, ref Index, new jsObject(), SingleObject);
        }

        public jsObject MatchAll(string Data, ref int Index) {
            return MatchAll(Data, ref Index, new jsObject());
        }

        public jsObject MatchAll(string Data, jsObject Storage) {
            int Index = 0;

            return MatchAll(Data, ref Index, Storage);
        }

        public jsObject MatchAll(string Data, jsObject Storage, bool SingleObject) {
            int Index = 0;

            return MatchAll(Data, ref Index, Storage, SingleObject);
        }

        public jsObject MatchAll(string Data, ref int Index, jsObject Storage) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, new jsObject()) {
                Index = Index,
                BlockCount = Handlers == null ?
                    0 : Handlers.Length
            };

            while (MatchPartial(Context)) {
                Storage.Add(Context.Storage);
                Context.Storage = new jsObject();
                Context.BlockIndex = 0;

                if (Context.IsDone())
                    break;
            }

            Index = Context.Index;
            return Storage;
        }

        public jsObject MatchAll(string Data, ref int Index, jsObject Storage, bool SingleObject) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, new jsObject()) {
                Index = Index,
                BlockCount = Handlers == null ?
                    0 : Handlers.Length
            };

            while (MatchPartial(Context)) {
                if (!SingleObject) {
                    Storage.Add(Context.Storage);
                }
                else if (Context.Storage.Count == 2) {
                    var Key = Context.Storage["Key"];
                    var Value = Context.Storage["Value"];

                    if (Key != null && Value != null)
                        Storage.Set(Key as string, Value);
                }
                else if (Context.Storage.Count == 1) {
                    var Value = Context.Storage["Value"];

                    if (Value != null)
                        Storage.Add(Value);
                }

                Context.Storage = new jsObject();
                Context.BlockIndex = 0;

                if (Context.IsDone())
                    break;
            }

            Index = Context.Index;
            return Storage;
        }

        private bool Match(Context Context) {
			return Match(Handlers, Context) && Context.IsDone();
		}

        private bool MatchPartial(Context Context) {
            return Match(Handlers, Context);
        }

        private static bool Match(Block[] Handlers, Context Context) {
            for (; Context.BlockIndex < Handlers.Length; Context.BlockIndex++) {
                var Current = Handlers[Context.BlockIndex];

                if (Context.IsDone()) {
                    if (Current is Optional && Context.BlockIndex == Handlers.Length - 1) return true;
                    break;
                }

                if (!Current.Match(Context)) {
                    if (Current is Optional)
                        continue;

                    return false;
                }
            }

			return Context.BlockIndex >= Handlers.Length;
        }

        private static Block[] Parse(string Fmt) {
            StringIterator It = new StringIterator(Fmt);
            List<Block> List = new List<Block>();

            Block Last = null;
            while (!It.IsDone()) {
                var f = Parse(It);

                if (f == null) 
                    break;

                if (Last != null) {
                    if (Last is Optional) {
                        (Last as Optional).Blocks.Last().Next = f;
                    }
                    else {
                        Last.Next = f;
                    }
                }

                List.Add(f);
                Last = f;
            }

            return List.ToArray();
        }

        private static Block Parse(StringIterator It) {
            switch (It.Current) {
                default:{
                    var Start = It.Index;
                    if (It.GotoFirstPossible(StringMatching.Tokens) == default(char)) {
                        It.Index = It.Length;
                        return new Static(It.Substring(Start, It.Length - Start));
                    }
                    else {
                        return new Static(It.Substring(Start, It.Index - Start));
                    }
                }

                case '{': {
                    It.Tick();

                    int Start, LastToken;
                    Start = LastToken = It.Index;

                    bool HasTests = false, HasMods = false;
                    string Name, Format, Tests, Mods;
                    Name = Format = Tests = Mods = null;
                    var Next = It.GotoFirstPossible(":", "->", "}");
                    if (Next == null) return null;

                    do {
                        switch (Next) {
                            case ":": {
                                Name = It.Substring(Start, It.Index - Start).Trim();
                                It.Tick();

                                LastToken = It.Index;
                                HasTests = true;

                                Next = It.GotoFirstPossible("->", "}");
                                if (Next == null) return null;
                                break;
                            }

                            case "->": {
                                if (HasTests) Tests = It.Substring(LastToken, It.Index - LastToken).Trim();
                                else Name = It.Substring(Start, It.Index - Start).Trim();

                                HasMods = true;
                                It.Consume("->");
                                LastToken = It.Index;

                                if (!It.Goto('{', '}')) return null;
                                else Next = "}";

                                break;
                            }

                            case "}": {
                                if (HasMods)
                                    Mods = It.Substring(LastToken, It.Index - LastToken).Trim();
                                else
                                if (HasTests)
                                    Tests = It.Substring(LastToken, It.Index - LastToken).Trim();
                                else
                                    Name = It.Substring(Start, It.Index - Start).Trim();

                                Format = It.Substring(Start, It.Index - Start);
                                It.Tick();

                                goto endOfExtract;
                            }
                        }
                    }
                    while (!It.IsDone());

                endOfExtract:
                    if (Tests == null && Mods == null)
                        return new Extract(Name); 
                    else
                        return new Extract(Format, Name, Tests, Mods);
                }

                case '`': {
                    It.Tick();

                    var Start = It.Index;
                    if (!It.Goto('`', '`'))
                        return null;

                    var Sub = It.Substring(Start, It.Index - Start);

                    It.Tick();
                    return new ExtractAll(Sub);
                }

                case '[': {
                    var Offset = It.Find('[', ']');
                    It.Tick();

                    var Grouping = It.Substring(It.Index, Offset - It.Index);

                    It.Index = ++Offset;
                    return new Optional(Grouping);
                }

                case '^':
                    It.Tick();
                    return new Whitespace();

                case '*':
                    It.Tick();
                    return new WildCard();

                case '?':
                    It.Tick();
                    return new WildChar();

                case '\\':
                    It.Tick();
                    return new Static(It.Current.ToString());
            }
        }
    }
}
