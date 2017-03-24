using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    public partial class Matcher {
        bool IsAnything;
        Block[] Blocks;
        
        public string Format { get; private set; }

        public Matcher(string Fmt) {
            Format = Fmt;
            Blocks = Parse(new StringIterator(Fmt));

            if (Blocks.Length == 1 && Blocks[0] is WildCard) {
                IsAnything = true;
            }
        }

        public bool Compare(string Data) {
            if (Blocks == null) return false;
            if (IsAnything) return true;

            int Index = 0;
            return Match(Blocks, Data, ref Index, _no) && Index >= Data.Length;
        }

        public JSON Match(string Data) {
            if (Blocks == null) return null;

            var Index = 0;
            var Storage = new JSON();
            if (IsAnything) return Storage;

            if (Match(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON Match(string Data, JSON Storage) {
            if (Blocks == null) return null;

            var Index = 0;
            if (IsAnything) return Storage;

            if (Match(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchAll(string Data) {
            if (Blocks == null) return null;

            var Index = 0;
            var Storage = new JSON();
            if (IsAnything) return Storage;

            if (MatchAll(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchAll(string Data, JSON Storage) {
            if (Blocks == null || Storage == null) return null;
            if (IsAnything) return Storage;

            int Index = 0;
            if (MatchAll(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchAllValues(string Data) {
            if (Blocks == null) return null;

            var Index = 0;
            var Storage = new JSON();
            if (IsAnything) return Storage;

            if (MatchAllValues(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchAllValues(string Data, JSON Storage) {
            if (Blocks == null || Storage == null) return null;
            if (IsAnything) return Storage;

            int Index = 0;
            if (MatchAllValues(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchKeyValuePairs(string Data) {
            if (Blocks == null) return null;

            var Index = 0;
            var Storage = new JSON();
            if (IsAnything) return Storage;

            if (MatchKeyValuePairs(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public JSON MatchKeyValuePairs(string Data, JSON Storage) {
            if (Blocks == null || Storage == null) return null;
            if (IsAnything) return Storage;

            int Index = 0;
            if (MatchKeyValuePairs(Blocks, Data, ref Index, Storage.Set) && Index >= Data.Length)
                return Storage;

            return null;
        }

        public string Template(JSON Storage) {
            var Output = new StringBuilder();

            if (Template(Output, Storage))
                return Output.ToString();

            return null;
        }

        public bool Template(StringBuilder Output, JSON Context) {
            if (Blocks != null)
            foreach (var b in Blocks) {
                if (!b.Template(Output, Context))
                    if (!b.IsOptional)
                        return false;
            }

            return true;
        }

        private bool Match(string Data, int Index, Action<string, object> f) {
			return Match(Blocks, Data, ref Index, f) && Index >= Data.Length;
		}

        private static bool Match(Block[] Blocks, string Data, ref int Index, Action<string, object> f) {
            int i;
            int L = Blocks.Length;

            for (i = 0; i < L; i++) {
                var Current = Blocks[i];

                if (!Current.Match(Data, ref Index, ref i, f)) {
                    return false;
                }
            }

            return i >= L;
        }

        private static bool MatchAll(Block[] Blocks, string Data, ref int Index, Action<string, object> f) {
            if (Blocks == null) return false;

            var i = 0;
            var Length = Data.Length;
            for (; Index < Length; i++) {
                var Storage = new JSON();

                if (!Match(Blocks, Data, ref Index, Storage.Set)) break;

                f(i.ToString(), Storage);
            }

            return i > 0;
        }

        private static bool MatchAllValues(Block[] Blocks, string Data, ref int Index, Action<string, object> f) {
            if (Blocks == null) return false;

            var i = 0;
            var Length = Data.Length;
            Action<string, object> _f = (k, v) => {
                if (string.Compare(k, "Value", StringComparison.Ordinal) == 0) {
                    f(i.ToString(), v);
                    i++;
                }
            };

            while (Index < Length) {
                if (!Match(Blocks, Data, ref Index, _f)) break;
            }

            return i > 0;
        }

        private static bool MatchKeyValuePairs(Block[] Blocks, string Data, ref int Index, Action<string, object> f) {
            if (Blocks == null) return false;

            string Key = null;
            object Value = null;

            Action<string, object> _f = (k, v) => {
                if (string.Compare(k, "Key", StringComparison.Ordinal) == 0) {
                    Key = v as string;
                }
                else
                if (string.Compare(k, "Value", StringComparison.Ordinal) == 0) {
                    Value = v;
                }
            };

            var i = 0;
            var Length = Data.Length;
            for (; Index < Length; i++) {
                if (!Match(Blocks, Data, ref Index, _f)) break;
                else f(Key, Value);
            }

            return i > 0;
        }

        private static void _no(string Key, object Value) { }

        private static Block[] Parse(StringIterator It) {
            List<Block> List = new List<Block>();

            Block Last = null;
            while (!It.IsDone()) {
                var f = ParseBlock(It);

                if (f == null) 
                    break;

                if (Last != null) {
                    if (f is Optional) {
                        Last.IsOptional = true;
                        continue;
                    }
                    else {
                        Last.Next = f;
                        Last.Prepare();
                    }
                }

                List.Add(f);
                Last = f;
            }

            if (Last != null) {
                Last.Prepare();
                Last.IsLast = true;
            }
            return List.ToArray();
        }

        private static Block ParseBlock(StringIterator It) {
            switch (It.Current) {
                default:{
                    var Start = It.Index;
                    if (It.GotoFirstPossible(StringMatching.Tokens) == default(char)) {
                        It.Index = It.LastIndex;
                        return new Static(It.Substring(Start, It.LastIndex - Start).Descape());
                    }
                    else {
                        return new Static(It.Substring(Start, It.Index - Start).Descape());
                    }
                }

                case '(': {
                    It.Tick();
                    var Start = It.Index;

                    if (It.Goto('(', ')')) {
                        var End = It.Index;
                        It.Tick();

                        return new Group(It.Clone(Start, End));
                    }
                    return null;
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

                case '?':
                    It.Tick();
                    return new Optional();
                
                case '^':
                    It.Tick();
                    return new Whitespace();

                case '*':
                    It.Tick();
                    return new WildCard();
            }
        }
        
        public static string operator| (Matcher m, JSON data) {
            return m.Template(data);
        }
    }
}
