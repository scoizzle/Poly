using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Group : Block {
            public bool KeyValuePairs;
            public bool MatchAll;

            public Block[] Blocks;

            private MatchDelegate Handler;

            public Group(StringIterator Str)
                : base(Str.ToString()) {

                KeyValuePairs = Str.Consume('!');
                MatchAll = Str.Consume('#');
                Blocks = Parse(Str);
            }

            internal override void Prepare() {
                if (KeyValuePairs) {
                    Handler = __KeyValuePairs;
                }
                else
                if (MatchAll) {
                    Handler = __MatchAll;
                }
                else {
                    Handler = __Default;
                }
            }

            internal override bool ValidCharacter(char Char) {
                return Blocks.FirstOrDefault()?.ValidCharacter(Char) == true;
            }

            public override bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                return Handler(Data, ref Index, ref BlockIndex, Store);
            }
            
            public override bool Template(StringBuilder Output, JSON Context){
                var Start = Output.Length;

                if (Blocks != null)
                foreach (var b in Blocks) {
                    if (!b.Template(Output, Context))
                        if (!IsOptional) {
                            return false;
                        }
                        else {
                            Output.Length = Start;
                            return true;
                        }
                }

                return true;
            }
            
            private bool __Default(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
				var Storage = new KeyValueCollection<object>();
                var Start = Index;
                var Result = Matcher.Match(Blocks, Data, ref Index, Storage.Set);

                if (Result) {
                    Storage.ForEach(Store);
                    return true;
                }

                Index = Start;
                return IsOptional;                
            }

            private bool __MatchAll(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
				var Storage = new KeyValueCollection<object>();
                var Result = Matcher.Match(Blocks, Data, ref Index, Storage.Set);

                if (Result) {
                    Storage.ForEach(Store);
                    return true;
                }

                return IsOptional;                
            }

            private bool __KeyValuePairs(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                int Start = Index;
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

                while (Index < Data.Length) {
                    if (Matcher.Match(Blocks, Data, ref Index, _f)) Store(Key, Value);
                    else break;
                }

                return Index > Start || IsOptional;
            }
        }
    }
}
