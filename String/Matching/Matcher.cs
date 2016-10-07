using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    public partial class Matcher {
        Block[] Blocks;

        public string Format { get; private set; }

        public Matcher(string Fmt) {
            Format = Fmt;

            try {
                Blocks = Parse(Fmt);
            } catch (Exception Error) {
                App.Log.Error(Error);
            }
        }

        public bool Compare(string Data) {
            if (Blocks == null) return false;
            return Match(new Context(Data, Blocks.Length));
        }

        public jsObject Match(string Data, jsObject Storage = null, int Index = 0) {
            if (Blocks == null) return null;
            if (Storage == null) Storage = new jsObject();

            var Context = new Context(Data, Blocks.Length, Index);

            if (Match(Context)) {
                Context.ExtractInto(Storage);
                return Storage;
            }

            return null;
        }

        public jsObject MatchAll(string Data, jsObject Storage = null, int Index = 0) {
            if (Blocks == null) return null;
            if (Storage == null) Storage = new jsObject();

            var Context = new Context(Data, Blocks.Length, Index, Data.Length);        
            var Extracts = new ManagedArray<Context.Extraction>(Context.Extractions);

            if (Extracts.Count > 0)
                Context.Extractions = new ManagedArray<Context.Extraction>();

            while (Matcher.Match(Blocks, Context)) {
                Extracts.Add(new Context.GroupedExtraction(Context.Extractions));

                Context.BlockIndex = 0;
                Context.Extractions.Clear();
            }

            if (Extracts.Count > 0) {
                Context.Extractions.CopyTo(Extracts);
                Context.Extractions = Extracts;
            }

            Context.ExtractInto(Storage);
            return Storage;
        }

        public jsObject MatchAllValues(string Data, jsObject Storage = null, int Index = 0) {
            if (Blocks == null) return null;
            if (Storage == null) Storage = new jsObject();

            var Context = new Context(Data, Blocks.Length, Index, Data.Length);
            var Extracts = new ManagedArray<Context.Extraction>(Context.Extractions);

            if (Extracts.Count > 0)
                Context.Extractions = new ManagedArray<Context.Extraction>();

            while (Matcher.Match(Blocks, Context)) {
                Extracts.Add(new Context.ValueExtraction(Context.Extractions));

                Context.BlockIndex = 0;
                Context.Extractions.Clear();
            }

            if (Extracts.Count > 0) {
                Context.Extractions.CopyTo(Extracts);
                Context.Extractions = Extracts;
            }

            Context.ExtractInto(Storage);
            return Storage;
        }

        public jsObject MatchKeyValuePairs(string Data, jsObject Storage = null, int Index = 0) {
            if (Blocks == null) return null;
            if (Storage == null) Storage = new jsObject();

            var Context = new Context(Data, Blocks.Length, Index, Data.Length);
            var Extracts = new ManagedArray<Context.Extraction>(Context.Extractions);

            if (Extracts.Count > 0)
                Context.Extractions = new ManagedArray<Context.Extraction>();

            while (Match(Blocks, Context)) {
                Extracts.Add(new Context.KeyValuePairExtraction(true, Context.Extractions));

                Context.BlockIndex = 0;
                Context.Extractions.Clear();
            }

            if (Extracts.Count > 0) {
                Context.Extractions.CopyTo(Extracts);
                Context.Extractions = Extracts;
            }

            Context.ExtractInto(Storage);
            return Storage;
        }

        private bool Match(Context Context) {
			return Match(Blocks, Context) && Context.IsDone();
		}

        private bool MatchPartial(Context Context) {
            return Match(Blocks, Context);
        }

        private static bool Match(Block[] Handlers, Context Context) {
            for (; Context.BlockIndex < Handlers.Length; Context.BlockIndex++) {
                var Current = Handlers[Context.BlockIndex];

                if (Context.IsDone()) {
                    if (Handlers.All(Context.BlockIndex, b => b is Optional))
                        return true;
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

                    var Sub = It.ExtractUntil('`');

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
