using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        static readonly char[] SpecialChars = new char[] {
            '{', '*', '?', '^', '\\', '[', '('
        };

        public delegate bool TestDelegate(char C);
        public delegate string ModDelegate(string Str);

        public static Dictionary<string, TestDelegate> Tests = new Dictionary<string, TestDelegate>() {
            { "Alpha", char.IsLetter },
            { "Numeric", char.IsNumber },
            { "AlphaNumeric", char.IsLetterOrDigit },
            { "Punctuation", char.IsPunctuation },
            { "Whitespace", char.IsWhiteSpace },
            { "NoWhitespace", c => { return !char.IsWhiteSpace(c); }}
        };

        public static Dictionary<string, ModDelegate> Modifiers = new Dictionary<string, ModDelegate>() {
            { "Escape", StringConversions.Escape },
            { "Descape", StringConversions.Descape },
            { "UrlEscape", StringConversions.Escape },
            { "UrlDescape", StringConversions.Descape },
            { "MD5", StringConversions.MD5 },
            { "SHA1", StringConversions.SHA1 },
            { "SHA256", StringConversions.SHA256 },
            { "SHA512", StringConversions.SHA512 },
            { "Base64Encode", StringConversions.Base64Encode },
            { "Base64Decode", StringConversions.Base64Decode },
            { "ToUpper", s => { return s.ToUpper(); }},
            { "ToLower", s => { return s.ToLower(); }}
        };

        private class MatchInfo {
            public bool IgnoreCase, Store;
            public StringIterator Data, Wild;

            public jsObject Result;

            public MatchInfo(string Data, string Wild, bool IgnoreCase, bool Store) {
                this.Store = Store;
                this.IgnoreCase = IgnoreCase;
                this.Data = new StringIterator(Data);
                this.Wild = new StringIterator(Wild);
                this.Result = new jsObject();
            }

            public MatchInfo(string Data, string Wild, bool IgnoreCase, bool Store, int DataLength, int WildLength) {
                this.Store = Store;
                this.IgnoreCase = IgnoreCase;
                this.Data = new StringIterator(Data, DataLength);
                this.Wild = new StringIterator(Wild, WildLength);
                this.Result = new jsObject();
            }

            public void Tick() {
                Data.Index++;
                Wild.Index++;
            }
        }

        public static bool Compare(this String Data, String Wild) {
            return Match(new MatchInfo(Data, Wild, false, false)) != null;
        }

        public static bool Compare(this String Data, String Wild, bool IgnoreCase) {
            return Match(new MatchInfo(Data, Wild, IgnoreCase, false)) != null;
        }

        public static bool Compare(this String Data, String Wild, bool IgnoreCase, int Index) {
            var Info = new MatchInfo(Data, Wild, IgnoreCase, true);

            Info.Data.Index = Index;

            return Match(Info) != null;
        }

        public static jsObject Match(this String Data, String Wild) {
            return Match(new MatchInfo(Data, Wild, false, true));
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase) {
            return Match(new MatchInfo(Data, Wild, IgnoreCase, true));
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase, int Index) {
            var Info = new MatchInfo(Data, Wild, IgnoreCase, true);

            Info.Data.Index = Index;

            return Match(Info);
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase, jsObject Storage) {
            var Info = new MatchInfo(Data, Wild, IgnoreCase, true);

            Info.Result = Storage;

            return Match(Info);
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase, int Index, jsObject Storage) {
            var Info = new MatchInfo(Data, Wild, IgnoreCase, true);

            Info.Data.Index = Index;
            Info.Result = Storage;

            return Match(Info);
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase, int Index, int DataLength, int WildIndex, int WildLength) {
            var Info = new MatchInfo(Data, Wild, IgnoreCase, true, DataLength, WildLength);

            Info.Data.Index = Index;
            Info.Wild.Index = WildIndex;

            return Match(Info);
        }

        public static jsObject MatchAll(this String Data, String Wild) {
            var Info = new MatchInfo(Data, Wild, true, true);
            var Result = new jsObject();

            while (Match(Info) != null) {
                if (Info.Result.Count > 1 && Info.Result.ContainsKey("Key")){
                    if (Info.Result.Count == 2 && Info.Result.ContainsKey("Value")) {
                        Result.Add(Info.Result["Key"].ToString(), Info.Result["Value"]);
                    }
                    else {
                        Result.Add(Info.Result["Key"].ToString(), Info.Result);
                    }
                }
                else {
                    Result.Add(Info.Result);
                }

                Info.Wild.Index = 0;
                Info.Result = new jsObject();
            }

            return Result;
        }

        private static jsObject Match(MatchInfo Info) {
            var Data = Info.Data;
            var Wild = Info.Wild;

            if (Wild.Length == 1 && Wild.String == "*")
                return Info.Result;

            while (Data.Index < Data.Length && Wild.Index < Wild.Length) {
                if (Data.IsAt(Wild[Wild.Index], Info.IgnoreCase) || Wild.IsAt('?')) {
                    Info.Tick();
                    continue;
                }

                if (Wild.IsAt('\\')){
                    if (Data.IsAt(Wild[Wild.Index + 1], Info.IgnoreCase)) {
                        Info.Tick();
                        Wild.Tick();
                        continue;
                    }
                    else {
                        return null;
                    }
                }

                if (Wild.IsAt('*')) {
                    Wild.Tick();

                    if (Wild.IsDone())
                        return Info.Result;

                    if (!GotoNextSection(Info))
                        return null;

                    continue;
                }

                if (Wild.IsAt('{')) {
                    Wild.Tick();
                    var WildCurrent = Wild.Index;

                    if (Wild.Goto('{', '}')) {
                        Wild.Tick();

                        string Value;
                        if (Wild.IsDone()) {
                            Value = Data.Substring(Data.Index);
                        }
                        else {
                            var DataCurrent = Data.Index;

                            if (GotoNextSection(Info)) {
                                Value = Data.String.Substring(DataCurrent, Data.Index - DataCurrent);
                            }
                            else return null;
                        }

                        if (!VerifyAndStoreData(Info, Wild.Substring(WildCurrent, Wild.Index - WildCurrent - 1), Value)) {
                            return null;
                        }

                        continue;
                    }

                    return null;
                }
                
                if (Wild.IsAt('[')) {
                    Wild.Tick();
                    var ClosingBracket = Wild.Find(']');

                    if (ClosingBracket != -1) {
                        Info.Wild = new StringIterator(Wild.String, ClosingBracket) { Index = Wild.Index };

                        Match(Info);

                        Info.Wild = Wild;
                        Wild.Index = ClosingBracket + 1;
                    }
                    continue;
                }

                if (Wild.IsAt('^')) {
                    Data.ConsumeWhitespace();
                    Wild.Tick();
                    continue;
                }

                break;
            }


            if (!Wild.IsDone())
                return null;

            return Info.Result;
        }

        private static bool VerifyAndStoreData(MatchInfo Info, string KeyAndExpression, string Value) {
            int x = KeyAndExpression.IndexOf(':'), 
                y = KeyAndExpression.IndexOf(':', x + 1);

            string Key;
            if (x == -1) {
                Key = KeyAndExpression.Substring(0, KeyAndExpression.Length);
                Info.Result.AssignValue(Key, Value);
                return true;
            }
            else {
                Key = KeyAndExpression.Substring(0, x);
            }

            string ValueTests;
            if (y == -1) {
                ValueTests = KeyAndExpression.Substring(x + 1, KeyAndExpression.Length - x - 1);
            }
            else {
                ValueTests = KeyAndExpression.Substring(x + 1, y - x - 1);
            }

            if (ValueTests.Length > 0) {
                string[] TestNames = ValueTests.Split(';');
                List<TestDelegate> TestFunctions = new List<TestDelegate>();

                foreach (var Name in TestNames) {
                    TestDelegate Delg;

                    if (Tests.TryGetValue(Name.Trim(), out Delg)) {
                        TestFunctions.Add(Delg);
                    }
                    else if (Name.Contains(',')) {
                        x = Name.IndexOf(',');
                        var Min = Name.Substring(0, x).Trim().ToInt();
                        var Max = Name.Substring(x).Trim().ToInt();

                        if (Value.Length < Min || Value.Length > Max)
                            return false;
                    }
                    else {
                        App.Log.Error("Unable to find Match Test: {0}".Template(Name));
                        return false;
                    }
                }

                bool IsOk = true;
                for (int i = 0, t; i < Value.Length && IsOk; i++) {
                    IsOk = false;

                    for (t = 0; t < TestNames.Length; t++) {
                        if (TestFunctions[t](Value[i])) {
                            IsOk = true;
                            break;
                        }
                    }
                }

                if (!IsOk)
                    return false;
            }

            object FinalValue;

            if (y > x) {
                string ValueModifiers = KeyAndExpression.Substring(y + 1);
                if (ValueModifiers[0] == '<' && ValueModifiers[ValueModifiers.Length - 1] == '>') {
                    FinalValue = MatchAll(Value, ValueModifiers.Substring(1, ValueModifiers.Length - 2));
                }
                else {
                    string[] ModNames = ValueModifiers.Split(';');

                    foreach (var Name in ModNames) {
                        ModDelegate Delg;

                        if (Modifiers.TryGetValue(Name.Trim(), out Delg)) {
                            Value = Delg(Value);
                        }
                        else {
                            App.Log.Error("Unable to find Match Modifier: {0}".Template(Name));
                            return false;
                        }
                    }

                    FinalValue = Value;
                }
            }
            else {
                FinalValue = Value;
            }

            if (Info.Store) {
                Info.Result.AssignValue(Key, FinalValue);
            }

            return true;
        }

        private static Tuple<char, int> FirstPossibleSpecialIndex(StringIterator It) {
            for (int i = It.Index, c; i < It.Length; i++) {
                for (c = 0; c < SpecialChars.Length; c++) {
                    if (It[i] == SpecialChars[c] && It[i - 1] != '\\') {
                        return new Tuple<char, int>(SpecialChars[c], i);
                    }
                }
            }

            return null;
        }

        private static bool GotoNextSection(MatchInfo Info) {
            var Index = FirstPossibleSpecialIndex(Info.Wild);
            var Length = (Index == null ? Info.Wild.Length : Index.Item2) - Info.Wild.Index;

            if (Length == 0 && Info.Wild.IsAt('^')) {
                Info.Data.ConsumeUntil(char.IsWhiteSpace);
                return true;
            }
            else {
                return Info.Data.Goto(
                    Info.Wild, 
                    Length, 
                    Info.IgnoreCase
                );
            }
        }
    }
}
