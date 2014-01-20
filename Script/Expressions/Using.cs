﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;

namespace Poly.Script.Node {
    public class Using : Expression {
        public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare("using", Index)) {
                var Delta = Index += 5;
                ConsumeWhitespace(Text, ref Delta);

                var Close = Delta;
                ConsumeValidName(Text, ref Close);

                if (Text.Compare(";", Close)) {
                    var Name = Text.Substring(Delta, Close - Delta);

                    if (Library.Defined.ContainsKey(Name)) {
                        Engine.Using.Add(Library.Defined[Name]);
                    }
                    else if (File.Exists(Name + ".dll")) {
                        Helper.ExtensionManager.Load(Name);
                    }
                    else {
                        App.Log.Error("Couldn't find library: " + Name);
                    }

                    Close += 1;
                    ConsumeWhitespace(Text, ref Close);

                    Index = Close;
                }
            }

            return null;
        }
    }
}
