using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly {
    using Data;

    public partial class Matcher {
        class Extract : Block {
            public string Key;
            public StringMatching.TestDelegate[] Tests;
            public StringMatching.ModDelegate[] Modifiers;
            
            private MatchDelegate Handler;

            public Extract(string key) : base(key) {
                Key = key;
            }

            public Extract(string fmt, string key, string test, string mod) : base(fmt) {
                Key = key;

                Tests = ParseTests(test);
                Modifiers = ParseMods(mod);
            }

            public override bool Match(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                return Handler(Data, ref Index, ref BlockIndex, Store);
            }

            public override bool Template(StringBuilder Output, JSON Context){
                var obj = Context[Key];
                if (obj == null) return IsOptional;
                Output.Append(obj);
                return true;
            }

            private object Modify(string value) {
                if (Modifiers == null)
                    return value;

                object Value = value;

                var i = 0;
                var Len = Modifiers.Length;

                do {
                    Value = Modifiers[i](value);
                    value = Value as string;
                }
                while (value != null && ++i < Len);

                return Value;
            }

            internal override bool ValidCharacter(char c) {
                if (Tests == null)
                    return true;

                var Len = Tests.Length;
                for (int i = 0; i < Len; i++) {
                    if (Tests[i](c)) return true;
                }
                return false;
            }

            internal override void Prepare() {
                if (Next == null) {
                    if (Tests == null) {
                        Handler = __UntilEnd;
                    }
                    else {
                        Handler = __UntilEnd_WithTests;
                    }
                }
                else if (Next is Static) {
                    if (Tests == null) {
                        Handler = __UntilStatic;
                    }
                    else {
                        Handler = __UntilStatic_WithTests;
                    }
                }
                else if (Next is Whitespace) {
                    if (Tests == null) {
                        Handler = __UntilWhitespace;
                    }
                    else {
                        Handler = __UntilWhitespace_WithTests;
                    }
                }
                else {
                    Handler = __Default;
                }
            }

            private bool ValidString(string str, int Start, int Stop) {
                if (Start < 0 || Start >= Stop)
                    return false;

                var len = Tests.Length;
                do {
                    var c = str[Start];
                    var i = 0;

                    while (i < len) {
                        if (Tests[i++](c))
                            goto Next;
                    }

                    if (i == len) return false;
                    Next: Start++;
                }
                while (Start < Stop);

                return true;
            }

            private bool __Default(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var Start = Index;

                while (Index < Data.Length) {
                    if (!ValidCharacter(Data[Index])) break;
                    Index++;
                }

                if (Index > Start) {
                    Store(Key, Modify(Data.Substring(Start, Index - Start)));
                    return true;
                }

                return IsOptional;
            }

            private bool __UntilWhitespace(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var Start = Index;

                while (Index < Data.Length) {
                    if (char.IsWhiteSpace(Data[Index])) break;
                    Index++;
                }

                if (Index > Start) {
                    Store(Key, Modify(Data.Substring(Start, Index - Start)));
                    return true;
                }

                return IsOptional;
            }

            private bool __UntilWhitespace_WithTests(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var Start = Index;

                while (Index < Data.Length) {
                    if (char.IsWhiteSpace(Data[Index])) break;
                    Index++;
                }

                if (Index > Start) {
                    if (ValidString(Data, Start, Index)) {
                        Store(Key, Modify(Data.Substring(Start, Index - Start)));
                        return true;
                    }
                }

                Index = Start;
                return IsOptional;
            }
            
            private bool __UntilStatic(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var S = Next.Format;
                var l = S.Length;
                var f = Data.Find(S, Index, Data.Length);

                if (f != -1) {
                    Store(Key, Modify(Data.Substring(Index, f - Index)));
                    Index = f + l;
                    BlockIndex++;
                    return true;
                }
                else
                if (Next.IsLast && Next.IsOptional) {
                    f = Data.Length;
                    Store(Key, Modify(Data.Substring(Index, f - Index)));
                    Index = f + l;
                    BlockIndex++;
                    return true;
                }

                return IsOptional;
            }

            private bool __UntilStatic_WithTests(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var S = Next.Format;
                var l = S.Length;
                var f = Data.Find(Data, Index);

                if (f != -1 && ValidString(Data, Index, f)) { 
                    Store(Key, Modify(Data.Substring(Index, f - Index)));
                    Index = f + l;
                    BlockIndex++;
                    return true;
                }
                else
                if (Next.IsLast && Next.IsOptional) {
                    f = Data.Length;

                    if (ValidString(Data, Index, f)) {
                        Store(Key, Modify(Data.Substring(Index, f - Index)));
                        Index = f + l;
                        BlockIndex++;
                        return true;
                    }
                }

                return IsOptional;
            }

            private bool __UntilEnd(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var l = Data.Length;

                if (Index < l) {
                    Store(Key, Modify(Data.Substring(Index, l - Index)));
                    Index = l;
                    return true;
                }

                return IsOptional;
            }

            private bool __UntilEnd_WithTests(string Data, ref int Index, ref int BlockIndex, Action<string, object> Store) {
                var l = Data.Length;

                if (Index < l && ValidString(Data, Index, l)) {
                    Store(Key, Modify(Data.Substring(Index, l - Index)));
                    Index = l;
                    return true;
                }

                return IsOptional;
            }

            private StringMatching.TestDelegate[] ParseTests(string Test) {
                if (Test == null)
                    return null;

                var It = new StringIterator(Test);
                var List = new List<StringMatching.TestDelegate>();

                while (!It.IsDone()) {
                    bool Not = It.Consume('!');

                    if (It.Consume('[')) {
                        var Chars = new StringBuilder();

                        while (!It.IsDone()) {
                            if (It.Current == ']')
                                break;

                            if (It.Current == '\\') {
                                It.Tick();

                                Chars.Append(It.Current);
                                It.Tick();
                            }
                            else {
                                var First = It.Current;
                                It.Tick();

                                if (It.Current == '-') {
                                    It.Tick();

                                    var Sec = It.Current;
                                    It.Tick();

                                    var f = CharRange(First, Sec);
                                    if (Not) List.Add(TestInvariant(f));
                                    else List.Add(f);
                                }
                                else {
                                    Chars.Append(First);
                                }
                            }
                        }
                        
                        if (It.Consume(']')) {
                            var f = ArrayContains(Chars.ToString().ToCharArray());

                            if (Not) List.Add(TestInvariant(f));
                            else List.Add(f);
                        }
                    }
                    else {
                        var Start = It.Index;

                        if (It.Consume(char.IsLetter)) {
                            var testName = It.Substring(Start, It.Index - Start);
                            var f = StringMatching.Tests.Get(testName);

                            if (f != null)
                                if (Not) List.Add(TestInvariant(f));
                                else List.Add(f);
                        }
                    }

                    It.Consume(char.IsWhiteSpace, C => C == ',');
                }

                if (List.Count > 0)
                    return List.ToArray();

                return null;
            }

            private StringMatching.ModDelegate[] ParseMods(string Mod) {
                if (Mod == null)
                    return null;

                var It = new StringIterator(Mod);
                var List = new List<StringMatching.ModDelegate>();

                while (!It.IsDone()) {
                    var Not = It.Consume('!');

                    if (It.Consume('`')) {
                        var Start = It.Index;

                        if (It.Goto('`', '`')) {
                            var modFmt = It.Substring(Start, It.Index - Start);
                            var matcher = new Matcher(modFmt);

                            if (Not) {
                                List.Add((o) => {
                                    var str = o as string;
                                    if (str != null) {
                                        return matcher.MatchKeyValuePairs(str, new Data.JSON());
                                    }
                                    return null;
                                });
                            }
                            else {
                                List.Add((o) => {
                                    var str = o as string;
                                    if (str != null) {
                                        return matcher.MatchAll(str, new Data.JSON());
                                    }
                                    return null;
                                });
                            }

                            It.Tick();
                        }
                        else break;
                    }
                    else {
                        var Start = It.Index;

                        if (It.Consume(char.IsLetterOrDigit)) {
                            var modName = It.Substring(Start, It.Index - Start);
                            var f = StringMatching.Modifiers.Get(modName);

                            if (f != null)
                                List.Add(f);
                        }
                        else break;
                    }

                    It.Consume(char.IsWhiteSpace, C => C == ',');
                }

                if (List.Count > 0)
                    return List.ToArray();

                return null;
            }

            private StringMatching.TestDelegate ArrayContains(char[] arr) {
                return (c) => {
                    var Len = arr.Length;
                    for (int i = 0; i < Len; i++)
                        if (arr[i] == c) return true;
                    return false;
                };
            }

            private StringMatching.TestDelegate CharRange(char First, char Last) {
                return c => c >= First && c <= Last;
            }

            private StringMatching.TestDelegate TestInvariant(StringMatching.TestDelegate f) {
                return c => !f(c);
            }
        }
    }
}
