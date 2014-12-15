using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Helpers;
    public class Using : Expression {
        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("using", Index)) {
                var Delta = Index += 5;
                ConsumeWhitespace(Text, ref Delta);

                var End = Delta;
                ConsumeValidName(Text, ref End);

                var Close = End;
                Text.ConsumeWhitespace(ref Close);

                if (Text.Compare("=", Close)) { 
                    var Name = Text.Substring(Delta, End - Delta);

                    Delta = Close + 1;
                    Text.ConsumeWhitespace(ref Delta);

                    Close = Delta;
                    ConsumeValidName(Text, ref Close);

                    var For = Text.Substring(Delta, Close - Delta);

                    if (Text.Compare(";", Close)) {
                        Engine.Shorthands[Name] = For;
                        Index = Close + 1;
                        return Expression.NoOperation;
                    }
                }
                else if (Text.Compare(";", Close)) {
                    var Name = Text.Substring(Delta, Close - Delta);

                    if (Library.Defined.ContainsKey(Name)) {
                        Engine.Using.Add(Library.Defined[Name]);
                    }
                    else if (File.Exists(Name + ".dll")) {
                        ExtensionManager.Load(Name);
                    }
                    else {
                        App.Log.Error("Couldn't find library: " + Name);
                    }

                    Close += 1;
                    ConsumeWhitespace(Text, ref Close);

                    Index = Close;
                    return Expression.NoOperation;
                }
            }

            return null;
        }
    }
}
