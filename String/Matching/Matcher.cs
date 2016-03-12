using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;
    public partial class Matcher {
        Block[] Handlers;

        readonly char[] Tokens = new char[] {
            '{', '*', '?', '^', '\\', '['
        };

        public Matcher(string Fmt) {
            Handlers = Parse(Fmt);
        }

        public jsObject Match(string Data) {
            int Index = 0;

            return Match(Data, ref Index, new jsObject());
        }

        public jsObject Match(string Data, ref int Index) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, new jsObject()) {
                Index = Index,
				BlockCount = Handlers == null ?
					0 : Handlers.Length
            };

            if (Match(Context)) {
                Index = Context.Index;
                return Context.Storage;
            }

            return null;
        }

        public jsObject Match(string Data, jsObject Storage) {
            int Index = 0;

            return Match(Data, ref Index, Storage);
        }

        public jsObject Match(string Data, ref int Index, jsObject Storage) {
            if (Handlers == null)
                return null;

            var Context = new Context(Data, Storage) {
				Index = Index,
				BlockCount = Handlers == null ?
					0 : Handlers.Length
            };

            if (Match(Context)) {
                Index = Context.Index;
                return Storage;
            }

            return null;
        }

        private bool Match(Context Context) {
            for (; Context.BlockIndex < Handlers.Length && !Context.IsDone(); Context.BlockIndex++) {
                var Current = Handlers[Context.BlockIndex];

                if (!Current.Match(Context)) {
                    if (Current is Optional)
                        continue;

                    return false;
                }
            }

            return Context.IsDone();
        }

        private Block[] Parse(string Fmt) {
            StringIterator It = new StringIterator(Fmt);
            List<Block> List = new List<Block>();

            Block Last = null;
            while (!It.IsDone()) {
                var f = Parse(It);

                if (f == null) 
                    break;

                if (Last != null)
                    Last.Next = f;

                List.Add(f);
                Last = f;
            }

            return List.ToArray();
        }

        private Block Parse(StringIterator It) {
            switch (It.Current) {
                default:{
                    var Start = It.Index;
                    if (It.FirstPossible(Tokens) == default(char)) {
                        It.Index = It.Length;
                        return new Static(It.Substring(Start, It.Length - Start));
                    }
                    else {
                        return new Static(It.Substring(Start, It.Index - Start));
                    }
                }

                case '{': {
                    var Offset = It.Find('{', '}');
                    It.Tick();

                    if (Offset == -1)
                        return null;

                    var Grouping = It.Substring(It.Index, Offset - It.Index);
                    var Keys = Grouping.Split(":").ToArray();

                    var Name = Keys[0];

                    var Tests = Keys.Length >= 2 ?
                        Keys[1] : string.Empty;

                    var Modifiers = Keys.Length == 3 ?
                        Keys[2] : string.Empty;

                    It.Index = ++Offset;
                    return new Extract(Name, Tests, Modifiers);
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
