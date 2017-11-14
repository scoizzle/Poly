namespace Poly.String.Matcher.Parsers {
    public static class Wildcard {
		static ExtractDelegate Handler = (it, get) => {
            it.Offset = it.LastIndex;
			return true;
		};

        static Matcher.TemplateDelegate Template = (it, get) => {
            return true;
        };

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out Matcher.TemplateDelegate template) {
			if (it.Consume('*')) {
				var has_next = Parser.Parse(
					it,
					out go_to,
					out _,
					out template);

                if (has_next) {
                    extract = go_to;
                }
                else { 
                    go_to = extract = Handler;
                    template = Template;
                }

				return true;
			}

			go_to = null;
			extract = null;
			template = null;
			return false;
		}

		public static bool Parse(
			StringIterator it,
			out ExtractDelegate go_to,
			out ExtractDelegate extract,
			out Matcher.TemplateDelegate template,
            ExtractDelegate goto_next,
            ExtractDelegate extract_next,
			Matcher.TemplateDelegate template_next)
        {
			if (it.Consume('*')) {
				var has_next = Parser.Parse(
					it,
					out ExtractDelegate next_goto,
					out ExtractDelegate next_extract,
					out Matcher.TemplateDelegate next_template,
					goto_next,
					extract_next,
					template_next
					);


				if (has_next) {
                    go_to = next_goto;
                    extract = next_goto;
                    template = next_template;
				}
				else {
                    go_to = goto_next;
                    extract = extract_next;
                    template = template_next;
				}

				return true;
			}

			go_to = null;
			extract = null;
			template = null;
			return false;
		}
    }
}
