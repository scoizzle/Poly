using System.Collections.Generic;
using System.Text;

namespace Poly.String.Matcher.Parsers {
		public static class Parser {
				public static bool Parse(
						StringIterator it,
						out ExtractDelegate go_to,
						out ExtractDelegate extract,
						out TemplateDelegate template) =>
									 Extract.Parse(it, out go_to, out extract, out template) ||
										 Group.Parse(it, out go_to, out extract, out template) ||
								Whitespace.Parse(it, out go_to, out extract, out template) ||
									Wildcard.Parse(it, out go_to, out extract, out template) ||
										Static.Parse(it, out go_to, out extract, out template);

				public static bool Parse(
						StringIterator it,
						out ExtractDelegate go_to,
						out ExtractDelegate extract,
						out TemplateDelegate template,
						ExtractDelegate next_goto,
						ExtractDelegate next_extract,
						TemplateDelegate next_template) =>
									 Extract.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
										 Group.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
								Whitespace.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
									Wildcard.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
										Static.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template);

				
		}
}


namespace Poly {
    using Data;

    public partial class Matcher {
		static bool Parse(StringIterator it, Context context) =>
			_Extraction.Parse(it, context) ||
			_Group.Parse(it, context) ||
			_Whitespace.Parse(it, context) ||
			_Wildcard.Parse(it, context) ||
			_Static.Parse(it, context);
	}
}