using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    public partial class _Matcher {
        delegate bool Handler(string Data, ref int Index, jsObject Storage);

        static readonly char[] TestModSplt = new char[] { ',', ';' };

        public string Format { get; private set; }
        private Handler[] Consumers;

        public _Matcher(string Format) {
            this.Format = Format;
            try {
                this.GenerateLists();
            }
            catch { }
        }

        public jsObject Match(string Data) {
            int Index = 0;

            return Match(Data, ref Index, new jsObject());
        }

        public jsObject Match(string Data, jsObject Storage) {
            int Index = 0;

            return Match(Data, ref Index, Storage);
        }

        public jsObject Match(string Data, ref int Index, jsObject Storage) {
            if (Consumers == null)
                return null;

            for (int i = 0; i < Consumers.Length && Index < Data.Length; i++) {
                if (!Consumers[i](Data, ref Index, Storage))
                    return null;
            }

            return Storage;
        }

        private void GenerateLists() {
            List<Handler> Handlers = new List<Handler>();
            StringIterator It = new StringIterator(Format);
            StringBuilder Current = new StringBuilder();

            while (!It.IsDone()) {
                var C = It.Current;

                switch (C) {
                    default:
                        Current.Append(C);
                        It.Tick();
                        break;

                    case '\\':
                        It.Tick();
                        Current.Append(It.Current);
                        It.Tick();
                        break;

                    case '*': {
                        Handlers.Add(ConsumeStatic(Current.ToString()));
                        Current.Clear();
                        It.Tick();

                        int Offset = It.Index;
                        var Next = It.FirstPossible(ref Offset, StringMatching.SpecialChars);

                        if (Next == default(char)) {
                            Handlers.Add(ConsumeUntilEnd);
                        }
                        else {
                            Handlers.Add(
                                ConsumeUntil(It.Substring(It.Index, Offset - It.Index))
                            );

                            It.Index = Offset;
                        }
                        break;
                    }

                    case '^': {
                        Handlers.Add(ConsumeStatic(Current.ToString()));
                        Current.Clear();
                        It.Tick();

                        Handlers.Add(ConsumeWhitespace);
                        break;
                    }

                    case '{': {
                        Handlers.Add(ConsumeStatic(Current.ToString()));
                        Current.Clear();
                        It.Tick();

                        var Offset = It.Find('}');

                        var Grouping = It.Substring(It.Index, Offset - It.Index);
                        var Keys = Grouping.Split(':');

                        var Name = Keys[0];
                        StringMatching.TestDelegate[] Tests = null;
                        StringMatching.ModDelegate[] Modifiers = null;

                        if (Keys.Length >= 2) {
                            Tests = ParseTests(Keys[1]);
                        }

                        if (Keys.Length == 3) {
                            Modifiers = ParseMods(Keys[2]);
                        }

                        It.Index = ++Offset;
                        var Next = It.FirstPossible(ref Offset, StringMatching.SpecialChars);
                        var Length = 0;

                        string NextSection = null;

                        if (!It.IsDone() && Next == default(char)) {
                            Offset = It.Length;
                        }
                        Length = Offset - It.Index;

                        if (Length > 0) {
                            NextSection = It.Substring(It.Index, Length);

                            Handlers.Add(
                                ExtractUntil(NextSection, Name, Tests, Modifiers)
                            );

                            Handlers.Add(ConsumeStatic(NextSection));
                            It.Index = Offset;
                        }
                        else {
                            Handlers.Add(
                                ExtractUntil(NextSection, Name, Tests, Modifiers)
                            );
                            It.Index = Offset;
                        }

                        break;
                    }

                    case '[': {
                        Handlers.Add(ConsumeStatic(Current.ToString()));
                        Current.Clear();

                        var Offset = It.Find('[', ']');

                        var Optional = It.Substring(It.Index, Offset - It.Index);
                        It.Index = Offset + 1;

                        Handlers.Add(ConsumeOptional(Optional));
                        break;
                    }
                }
            }

            Handlers.Add(ConsumeStatic(Current.ToString()));
            Consumers = Handlers.ToArray();
        }

        private StringMatching.TestDelegate[] ParseTests(string Test) {
            var List = new List<StringMatching.TestDelegate>();

            StringMatching.TestDelegate Current;
            foreach (var Name in Test.Split(TestModSplt, StringSplitOptions.RemoveEmptyEntries)) {
                var Key = Name[0] == '!' ? 
                    Name.Substring(1) : 
                    Name;

                if (StringMatching.Tests.TryGetValue(Key, out Current)) { 
                    if (Name[0] == '!') {
                        List.Add((c) => {
                            return !Current(c);
                        });
                    }
                    else {
                        List.Add(Current);
                    }
                }
            }

            if (List.Count > 0)
                return List.ToArray();

            return null;
        }

        private StringMatching.ModDelegate[] ParseMods(string Mod) {
            var List = new List<StringMatching.ModDelegate>();

            StringMatching.ModDelegate Current;
            foreach (var Name in Mod.Split(TestModSplt, StringSplitOptions.RemoveEmptyEntries)) {
                if (StringMatching.Modifiers.TryGetValue(Name, out Current)) {
                    List.Add(Current);
                }
            }

            if (List.Count > 0)
                return List.ToArray();

            return null;
        }

        static Handler ConsumeUntilEnd = (string Data, ref int Index, jsObject Storage) => {
            Index = Data.Length;
            return true;
        };

        static Handler ConsumeWhitespace = (string Data, ref int Index, jsObject Storage) => {
            Data.ConsumeWhitespace(ref Index);
            return true;
        };

        static Handler ConsumeUntilWhitespace = (string Data, ref int Index, jsObject Storage) => {
            Data.ConsumeUntil(ref Index, char.IsWhiteSpace);
            return true;
        };

        static Handler ConsumeUntilPossible(params Handler[] Possible) {
            return (string Data, ref int Index, jsObject Storage) => {
                int I = Index;
                var S = new jsObject();

                for (int i = 0; i < Possible.Length; i++) {
                    if (Possible[i](Data, ref I, S)) {
                        Index = I;
                        S.CopyTo(Storage);
                        break;
                    }
                }

                return true;
            };
        }

        static Handler ConsumeOptional(string Format) {
            _Matcher Optional = new _Matcher(Format);

            return (string Data, ref int Index, jsObject Storage) => {
                int I = Index;
                var S = new jsObject();

                if (Optional.Match(Data, ref I, S) != null) {
                    S.CopyTo(Storage);
                    Index = I;
                }
                return true;
            };
        }

        static Handler ConsumeStatic(string Static) {
            return (string Data, ref int Index, jsObject Storage) => {
                return Data.Consume(Static, ref Index);
            };
        }

        static Handler ExtractUntil(string Static, string Key, StringMatching.TestDelegate[] Tests, StringMatching.ModDelegate[] Modifiers) {
            var Finder = ConsumeUntil(Static);
            return (string Data, ref int Index, jsObject Storage) => {
                var Next = Index;

                if (!Finder(Data, ref Next, Storage))
                    return false;

                return Extract(Next, Key, Tests, Modifiers)(Data, ref Index, Storage);
            };
        }

        static Handler ExtractUntil(Handler[] Possible, string Key, StringMatching.TestDelegate[] Tests, StringMatching.ModDelegate[] Modifiers) {
            var Finder = ConsumeUntilPossible(Possible);

            return (string Data, ref int Index, jsObject Storage) => {
                var Next = Index;

                if (!Finder(Data, ref Next, Storage))
                    return false;

                return Extract(Next, Key, Tests, Modifiers)(Data, ref Index, Storage);
            };
        }

        static Handler Extract(int Next, string Key, StringMatching.TestDelegate[] Tests, StringMatching.ModDelegate[] Modifiers) {
            return (string Data, ref int Index, jsObject Storage) => {
                if (Next < 0 || Next > Data.Length)
                    return false;

                var Value = Data.Substring(Index, Next - Index);

                if (Tests != null) {
                    int i, t;
                    bool Valid = false;

                    for (i = 0; i < Value.Length; i++) {
                        for (t = 0; t < Tests.Length; t++) {
                            if (Tests[t](Value[i]) == true) {
                                Valid = true;
                                break;
                            }
                        }

                        if (!Valid)
                            return false;
                    }
                }

                if (Modifiers != null) {
                    for (int i = 0; i < Modifiers.Length; i++) {
                        Value = Modifiers[i](Value);
                    }
                }

                Storage.Set(Key, Value);
                Index = Next;
                return true;
            };
        }

        static Handler ConsumeUntil(string Static) {
            return (string Data, ref int Index, jsObject Storage) => {
                return (Index = Data.Find(Static, Index)) != -1;
            };
        }
    }
}
