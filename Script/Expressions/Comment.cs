using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;

    class Comment : Expression {
		new public static Node Parse(Engine Engine, StringIterator It) {
			if (It.IsAt ("/*")) {
				if (!It.Goto ("*/"))
					It.Index = It.Length;
				else
					It.Consume ("*/");
				
				return Expression.NoOperation;
			} 
			else if (It.IsAt ("//") || It.IsAt ('#')) {
				if (!It.Goto (Environment.NewLine))
					It.Index = It.Length;
				else
					It.Consume (Environment.NewLine);

				return Expression.NoOperation;
			}
			return null;
        }
    }
}
