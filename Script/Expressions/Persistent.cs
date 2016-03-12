using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
	using Nodes;
	using Types;

	public class Persist : Expression {
		public static new Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
			if (!IsParseOk(Engine, Text, ref Index, LastIndex))
				return null;

			var Delta = Index;
			if (Text.Consume("persist", ref Delta)) {
				Text.ConsumeWhitespace (ref Delta);

				var Name = String.Parse(Engine, Text, ref Delta, LastIndex);

				if (Name is StaticValue) {
					ConsumeWhitespace (Text, ref Delta);

					if (Text.Consume ("->", ref Delta)) {
						ConsumeWhitespace(Text, ref Delta);

						var Var = Variable.Parse (Engine, Text, ref Delta, LastIndex);
						ConsumeWhitespace (Text, ref Delta);

						if (Var != null && (Var.IsGlobal || Var.IsStatic)) {
							Index = Delta;

							var FileName = Engine.IncludePath + Name.ToString ();

							Engine.PersistentFiles.Add (FileName,
								new Helpers.PersistentFile (FileName, Var)
							);

							return Expression.NoOperation;
						}
					}
				}
			}

			return null;
		}
	}
}
