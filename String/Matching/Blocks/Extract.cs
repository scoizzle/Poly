using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public partial class Matcher {
        class Extract : Block {
            static readonly char[] TestModSplt = new char[] { ',', ';' };

            public string Key;
            public StringMatching.TestDelegate[] Tests;
            public StringMatching.ModDelegate[] Modifiers;

            public Extract(string Name, string Tests, string Mods)
                : base(string.Join(":", Name, Tests, Mods)) {

                this.Key = Name;
                this.Tests = ParseTests(Tests);
                this.Modifiers = ParseMods(Mods);
            }

            public override bool Match(Context Context) {
                var Start = Context.Index;
                var Length = 0;

                if (Tests == null && Next == null) {
                    Length = Context.Length - Start;
                    Context.Index = Context.Length;
                }
                else if (Next is Static) {
                    Length = Context.Find(Next.Format);

                    if (Length == -1)
                        return false;

                    if (Tests != null)
                    for (var i = Context.Index; i < Length; i++) {
                        if (!ValidChar(Context[i]))
                            return false;
                    }

                    Length -= Start;

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
                    while (!Context.IsDone()) {
                        if (Tests != null && ValidChar(Context.Current)) {
                            Context.Tick();
                            Length++;
                        }
                        else if (Next != null && Next.Match(Context)) {
                            Context.BlockIndex++;

                            if (Length == 0)
                                return true;

                            break;
                        }
                        else break;
                    }
                }
                

                if (Length == 0) {
                    if (Context.IsDone())
                        return true;
                    return false;
                }
                else {
                    if (Context.Store)
                        Context.Storage.Set(Key, Modify(Context.Substring(Start, Length)));
                }

                return true;
            }

            public bool ValidChar(char c) {
                bool Result = false;

                for (int i = 0; i < Tests.Length; i++) {
                    var R = Tests[i](c);

                    if (R == true)
                        return true;
                    else
                    if (R == false)
                        return false;
                    else
                    if (R == null)
                        Result = true;
                }

                return Result;
            }

            private string Modify(string Value) {
                if (Modifiers != null) {
                    for (int i = 0; i < Modifiers.Length; i++) {
                        Value = Modifiers[i](Value);
                    }
                }

                return Value;
            }
            
            private StringMatching.TestDelegate[] ParseTests(string Test) {
                var List = new List<StringMatching.TestDelegate>();

                foreach (var Name in Test.Split(TestModSplt, StringSplitOptions.RemoveEmptyEntries)) {
                    bool Not = Name[0] == '!';
                    var Key = Not ?
                        Name.Substring(1) :
                        Name;

                    StringMatching.TestDelegate Current;
                    if (Key.StartsWith("[") && Key.EndsWith("]")) {
						var Split = Key.Substring(1, Key.Length - 2).Replace("\\:", ":").ToCharArray();

                        if (Not) {
                            List.Add((c) => {
                                if (Split.Contains(c))
                                    return false;
                                return null;
                            });
                        }
                        else {
                            List.Add(c => Split.Contains(c));
                        }
                    }
                    else
                    if (StringMatching.Tests.TryGetValue(Key, out Current)) {
                        if (Not) {
                            List.Add((c) => {
                                if (Current(c) == true)
                                    return false;
                                return null;
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
        }
    }
}
