using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly {
    public partial class Matcher {
        class Extract : Block {
            public string Key;
            public StringMatching.TestDelegate[] Tests;
            public StringMatching.ModDelegate[] Modifiers;

            public Extract(string key) : base(key) {
                Key = key;
            }

            public Extract(string fmt, string key, string test, string mod) : base(fmt) {
                Key = key;

                Tests = ParseTests(test);
                Modifiers = ParseMods(mod);
            }

            public override bool Match(Context Context) {
                var Start = Context.Index;
                var Length = 0;

                if (Tests == null && Next == null) {
                    Length = Context.Length - Start;
                    Context.Index = Context.Length;
                }
                else if (Next is Static) {
                    if (Tests != null) {
                        var Format = Next.Format;
                        var String = Context.String;
                        var Last = Context.Length;

                        var idx = String.Find(Format, Start, Last);

                        if (idx == -1 || !ValidString(String, Start, idx))
                            return false;                        
                        
                        Length = idx - Start;
                    }
                    else {
                        Length = Context.Find(Next.Format);

                        if (Length == -1)
                            return false;

                        Length -= Start;
                    }

                    Context.Index += Length + Next.Format.Length;
                    Context.BlockIndex++;
                }
				else if (Next is WildCard || Next is WildChar || Next is Optional) {
                    if (Tests == null) {
                        Context.Index = Context.Length;
                    }
                    else {
                        for (var i = Context.Index; i < Context.Length; i++) {
                            if (!ValidChar(Context[i]))
                                break;
                            Length++;
                        }
                        Context.Index += Length;
                    }
                }
                else if (Next is Whitespace) {
                    Context.ConsumeUntil(char.IsWhiteSpace);
                    Length = Context.Index - Start;
                }
                else {
                    if (Tests != null) {
                        while (!Context.IsDone() && ValidChar(Context.Current)) {
                            Context.Tick();
                            Length++;
                        }
                    }
                    else
                    if (Next != null) {
                        if (Next.Match(Context)) {
                            Context.BlockIndex++;

                            if (Length == 0)
                                return true;
                        }
                    }
                }                

                if (Length == 0) {
                    if (Context.IsDone())
                        return true;
                    return false;
                }
                else {
                    Context.AddExtraction(Key, Start, Length, Modifiers);
                }

                return true;
            }

            public bool ValidChar(char c) {
                return Tests.Any(f => f(c));
            }

            public bool ValidString(string str, int Start, int Stop) {
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
                                        return matcher.MatchKeyValuePairs(str);
                                    }
                                    return null;
                                });
                            }
                            else {
                                List.Add((o) => {
                                    var str = o as string;
                                    if (str != null) {
                                        return matcher.MatchAll(str);
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
                return arr.Contains;
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
