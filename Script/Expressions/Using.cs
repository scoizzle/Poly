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
		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.Consume ("using")) {
				It.ConsumeWhitespace ();

				var Start = It.Index;
				if (It.Consume (NameFuncs)) {
					var Name = It.Substring (Start, It.Index - Start);

					It.ConsumeWhitespace ();
					if (It.Consume (':')) {
						It.ConsumeWhitespace ();

						Start = It.Index;

						if (It.Consume (c => c == '.', char.IsLetterOrDigit)) {
							var Type = It.Substring (Start, It.Index - Start);
							Engine.ReferencedTypes [Name] = SystemTypeGetter.GetType (Type);
							return Expression.NoOperation;
						}
					} else {
						if (Library.Defined.ContainsKey (Name))
							Engine.Usings.Add (Library.Defined [Name]);
						else if (File.Exists (Name + ".dll"))
							ExtensionManager.Load (Name);
						else {
							App.Log.Error("Couldn't find library: " + Name);
							return null;
						}

						return Expression.NoOperation;
					}
				}
			}
			return null;
		}
    }
}
