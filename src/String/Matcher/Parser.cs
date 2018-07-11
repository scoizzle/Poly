using System.Collections.Generic;
using System.Text;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
		protected static bool Parse(StringIterator it, Context context) =>
			Extraction.Parse(it, context) ||
			Group.Parse(it, context) ||
			Whitespace.Parse(it, context) ||
			Wildcard.Parse(it, context) ||
			Static.Parse(it, context);
	}
}