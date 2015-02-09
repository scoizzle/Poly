using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Data;

namespace Poly {
    public static class StringMatching {
        public static readonly char[] WildChars = new char[] {
            '{', '*', '?', '^', '\\', '[', '('
        };

        public static jsObject<Func<string, string>> Modifiers = new jsObject<Func<string, string>>() {
            { "escape", (V) => { return V.Escape(); }},
            { "descape", (V) => { return V.Descape(); }},
            { "uriEscape", (V) => { return Uri.EscapeDataString(V); }},
            { "uriDescape", (V) => { 
                return Regex.Replace(V.Replace("+", " "), "%([A-Fa-f\\d]{2})", a => "" + Convert.ToChar(Convert.ToInt32(a.Groups[1].Value, 16)));
            }},
            { "toUpper", (V) => { return V.ToUpper(); }},
            { "toLower", (V) => { return V.ToLower(); }},
        };

        public static Dictionary<char, Func<char, bool>> MatchValidityCheckers = new Dictionary<char, Func<char, bool>>() {
            { 'a', char.IsLetter },
            { 'n', char.IsNumber },
            { 'p', char.IsPunctuation },
            { 'w', char.IsWhiteSpace }
        };

        private static jsObject Obj = new jsObject();
        
        public static bool Compare(this String This, String Wild, bool IgnoreCase = false, int Index = 0) {
            return Match(This, Wild, IgnoreCase, Obj, 0, false) != null;
        }

        public static jsObject Match(this String Data, String Wild, bool IgnoreCase = false, jsObject Storage = null, int Index = 0, bool Store = true) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
                return null;

            int Offset = 0, DataLen = Data.Length, Wildlen = Wild.Length;
            return Match(Data, Wild, ref Index, ref Offset, IgnoreCase, Storage, Store, DataLen, Wildlen);
        }

		public static jsObject MatchAll(this String Data, String Wild, bool IgnoreCase = false, int Index = 0) {
			if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(Wild))
				return null;

			jsObject Storage = new jsObject ();

			while (Index < Data.Length) {
				var Offset = 0;
				var Node = new jsObject ();

				if (Match(Data, Wild, ref Index, ref Offset, IgnoreCase, Node, true, Data.Length - Index, Wild.Length) != null)
					Storage.Add(Node);
				else
					break;
			}

			return Storage;
		}

        public static jsObject MatchChain(this String Data, params String[] Wilds) {
			if (string.IsNullOrEmpty(Data))
				return null;

            var Index = 0;
            var Storage = new jsObject();

            while (Index < Data.Length) {
                var Node = new jsObject();
                var Delta = Index;
                var Offset = 0;

                foreach (var Wild in Wilds) {
                    Node = Match(Data, Wild, ref Delta, ref Offset, true, Node, true, Data.Length, Wild.Length);

                    if (Node != null && Node.Count != 0) {
                        Storage.Add(Node);
                        Index = Delta;
                        break;
                    }
                }

                if (Node == null)
                    return null;

                Data.ConsumeWhitespace(ref Index);
            }

            return Storage;
        }

        private static jsObject Match(this String This, String Wild, ref int Index, ref int Offset, bool IgnoreCase = false, jsObject Storage = null, bool Store = true, int DataLen = 0, int WildLen = 0) {
            if (string.IsNullOrEmpty(This) || string.IsNullOrEmpty(Wild))
                return null;

            if (WildLen == 1 && DataLen > 1 && Wild != "*")
                return null;
            
            if (Store && Storage == null)
                Storage = new jsObject();

            if (WildLen == 2 && Wild[0] == '*'){
                if (This[DataLen - 1] == Wild[1]) {
                    return Storage;
                }
                else {
                    return null;
                }
            }

            while (Index < DataLen && Offset < WildLen) {
                var C = Wild[Offset];

				if (!WildChars.Contains(C) && (C == This[Index] || C == '?')) {
                    Index++; Offset++;
                    continue;
                }

                switch (C) {
                    case '\\':
                        if (This[Index] == Wild[Offset + 1])
                            Index++; Offset += 2;
                        break;
                    case '*':
                        if (++Offset == WildLen)
                            return Storage;

                        if (Wild[Offset] == '\\')
                            Offset++;

                        if (!FindNextSection(This, Wild, ref Index, ref Offset, IgnoreCase))
                            return null;
                        break;
                    case '{':
                        int NameEnd = Offset;

                        if (Wild.FindMatchingBrackets("{", "}", ref Offset, ref NameEnd)) {
                            int Sub = Index,
                                SubW = NameEnd + 1;

							if (SubW == WildLen) {
								Sub = DataLen;
							}
							else {
								if (!FindNextSection(This, Wild, ref Sub, ref SubW, IgnoreCase))
									return null;
							}

                            string Key = "", Value = "";

                            if (!VerifyAndExtractData(This, Wild, Index, Offset, Sub, SubW - 1, ref Key, ref Value))
                                return null;

                            if (Store) {
                                Storage.Set(Key, Value);
                            }

                            Index = Sub;
                            Offset = SubW;

                            break;
                    	}
                    	return null;
                    case '[':
                        int OptEnd = Offset;

                        if (Wild.FindMatchingBrackets("[", "]", ref Offset, ref OptEnd)) {
                            var Opt = Match(This, Wild, ref Index, ref Offset, IgnoreCase, null, Store, DataLen, OptEnd);

                            if (Opt != null) {
                                Opt.CopyTo(Storage);
                            }

                            Offset = OptEnd + 1;
                        }
                        break;
					case '(':
						int End = Offset;
						if (Wild.FindMatchingBrackets("(", ")", ref Offset, ref End)) {
							int Dend = Index;
							if (This.FindMatchingBrackets("(", ")", ref Index, ref Dend)) {
								if (Match(This, Wild, ref Index, ref Offset, IgnoreCase, Storage, Store, Dend + 1, End) == null)
									return null;
								continue;
							}
						}

						if (C == This [Index] || C == '?') {
							Index++;
							Offset++;
							continue;
						}
						break;
					case '^':
						This.ConsumeWhitespace(ref Index);
						Offset++;
						break;
                    default:
                        return null;
                }
            }

            if ((WildLen - Offset) != 0)
                return null;

            return Storage;
        }

        private static bool VerifyAndExtractData(String Data, String Wild, int Index, int Offset, int DLen, int WLen, ref string Key, ref string Value) {
            int NameEnd = Wild.Find(':', Offset, WLen), LimitEnd = -1;

            if (NameEnd == -1) {
                Key = Wild.Substring(Offset, WLen - Offset);
                Value = Data.Substring(Index, DLen - Index);
                return true;
            }
            else {
                Key = Wild.Substring(Offset, NameEnd - Offset);
                LimitEnd = Wild.Find(':', NameEnd + 1, WLen);
            }

            if (LimitEnd == -1)
                LimitEnd = WLen;

            if ((LimitEnd - NameEnd) > 1) {
                for (int I = Index; I < DLen; I++) {
                    bool Valid = false;
					var D = Data [I];
                    for (int iC = NameEnd + 1; iC < LimitEnd; iC++) {
                        var C = Wild[iC];
						Func<char, bool> Handler;

						if (MatchValidityCheckers.TryGetValue(C, out Handler))
						if (Valid = Handler(D))
							break;
                    }

                    if (!Valid)
                        return false;
                }
            }

            Value = Data.Substring(Index, DLen - Index);

            if (Wild.Contains('e', NameEnd + 1, LimitEnd) && string.IsNullOrWhiteSpace(Value)) {
                return false;
            }

            if (LimitEnd < WLen) {
                Func<string, string> Modder;
                foreach (var Mod in Wild.Split(",", LimitEnd + 1, WLen)) {
                    if (Modifiers.TryGet(Mod, out Modder)) {
                        Value = Modder(Value);
                    }
                }
            }
            return true;
        }

        private static bool FindNextSection(String This, String Wild, ref int Index, ref int WildIndex, bool IgnoreCase = false) {
            int NextToken = -1;
            
            for (int I = WildIndex; I < Wild.Length; I++){
                if (WildChars.Contains(Wild[I])) {
                    NextToken = I;
                    break;
                }
            }

            if (NextToken == -1) {
                NextToken = Wild.Length;
            }
			else if (Wild [NextToken] == '^' && (Wild.Length - NextToken) != 1) {
				ConsumeUntilWhitespaceOr(This, Wild[NextToken + 1], ref Index);
				return true;
			}

            if ((NextToken - WildIndex) == 0) {
                WildIndex = Wild.Length;
                Index = This.Length;
                return true;
            }

            int NextBlock = This.FindSubstring(Wild, Index, WildIndex, NextToken - WildIndex, IgnoreCase);

            if (NextBlock == -1) {
                if (NextToken == Wild.Length && Wild[Wild.Length - 1] == ']') {
                    Index = This.Length;
                    return true;
                }
                return false;
            }                

            Index = NextBlock;
            return true;
        }

		private static void ConsumeUntilWhitespaceOr(String This, char Or, ref int Index){
			if (!string.IsNullOrEmpty(This))
				for (; Index < This.Length; Index++)
					if (char.IsWhiteSpace(This [Index]))
						break;
					else if (This [Index] == Or)
						break;
		}
    }
}
