namespace Poly.String.Matcher.Parsers {
    using Data;

	public static class Extract {
		static ExtractDelegate Handler(string key) {
			return (it, set) => {
                var value = it.ToString();

                if (set(key, value)) {
                    it.Offset = it.LastIndex;
                    return true;
                }

                return false;
			};
		}

		static ExtractDelegate Handler(string key, ExtractDelegate next) {
			return (it, set) => {
				if (next(it, set)) {
					var value = it.ToString();

					if (set(key, value))
                        return true;
                }

				return false;
			};
		}

        static ExtractDelegate Handler(string key, Serializer serializer) {
			return (it, set) => {
                if (serializer.DeserializeObject(it, out object value)) {
					if (set(key, value)) {
						it.Offset = it.Index;
						return true;
					}
                }

				return false;
			};
		}

		static ExtractDelegate Handler(string key, Serializer serializer, ExtractDelegate next) {
			return (it, set) => {
				if (next(it, set)) {
					if (serializer.DeserializeObject(it, out object value)) {
						if (set(key, value)) {
							return true;
						}
					}
				}

				return false;
			};
		}

		static ExtractDelegate OptionalHandler(string key) {
			return (it, set) => {
				var value = it.ToString();

				if (set(key, value))
					it.Offset = it.LastIndex;

				return true;
			};
		}

		static ExtractDelegate OptionalHandler(string key, ExtractDelegate next) {
			return (it, set) => {
				if (next(it, set)) {
                    set(key, it.ToString());
				}

				return true;
			};
		}

		static ExtractDelegate OptionalHandler(string key, Serializer serializer) {
			return (it, set) => {
				if (serializer.DeserializeObject(it, out object value)) {
					if (set(key, value))
						it.Offset = it.Index;
				}

				return true;
			};
		}

		static ExtractDelegate OptionalHandler(string key, Serializer serializer, ExtractDelegate next) {
			return (it, set) => {
				if (next(it, set)) {
					if (serializer.DeserializeObject(it, out object value)) {
                        set(key, value);
					}
				}

				return true;
			};
		}

        static Matcher.TemplateDelegate Templater(string key) {
            return (it, get) => {
                if (get(key, out object value)) {
                    it.Append(value);
                    return true;
                }

                return false;
            };
        }

        static Matcher.TemplateDelegate Templater(string key, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value)) {
                    it.Append(value);

                    return next(it, get);
                }

                return false;
            };
        }

        static Matcher.TemplateDelegate Templater(string key, Serializer serializer) {
            return (it, get) => {
                if (get(key, out object value)) {
                    if (serializer.SerializeObject(it, value))
                        return true;
                }

                return false;
            };
        }

        static Matcher.TemplateDelegate Templater(string key, Serializer serializer, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value)) {
                    if (serializer.SerializeObject(it, value))
                        return next(it, get);
                }

                return false;
            };
        }

        static Matcher.TemplateDelegate OptionalTemplater(string key) {
            return (it, get) => {
                if (get(key, out object value))
                    it.Append(value);

                return true;
            };
        }

        static Matcher.TemplateDelegate OptionalTemplater(string key, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value))
                    it.Append(value);

                return next(it, get);
            };
        }

        static Matcher.TemplateDelegate OptionalTemplater(string key, Serializer serializer) {
            return (it, get) => {
                if (get(key, out object value))
                    serializer.SerializeObject(it, value);

                return true;
            };
        }

        static Matcher.TemplateDelegate OptionalTemplater(string key, Serializer serializer, Matcher.TemplateDelegate next) {
            return (it, get) => {
                if (get(key, out object value))
                    serializer.SerializeObject(it, value);

                return next(it, get);
            };
        }

        static bool parse_internal(
            string key,
            string type,
            bool optional,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out Matcher.TemplateDelegate template,
            bool has_next,
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
            Matcher.TemplateDelegate next_template) 
        {
			var serial = Serializer.GetCached(type);

			if (serial == null) {
				if (optional) {
					if (has_next) {
						go_to = extract = OptionalHandler(key, next_goto);
						template = OptionalTemplater(key, next_template);
					}
					else {
						go_to = extract = OptionalHandler(key);
						template = OptionalTemplater(key);
					}
				}
				else {
					if (has_next) {
						go_to = extract = Handler(key, next_goto);
						template = Templater(key, next_template);
					}
					else {
						go_to = extract = Handler(key);
						template = Templater(key);
					}
				}
			}
			else {
				if (optional) {
					if (has_next) {
						go_to = extract = OptionalHandler(key, serial, next_goto);
						template = OptionalTemplater(key, serial, next_template);
					}
					else {
						go_to = extract = OptionalHandler(key, serial);
						template = OptionalTemplater(key, serial);
					}
				}
				else {
					if (has_next) {
						go_to = extract = Handler(key, serial, next_goto);
						template = Templater(key, serial, next_template);
					}
					else {
						go_to = extract = Handler(key, serial);
						template = Templater(key, serial);
					}
				}
			}

			return true;
        }

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out Matcher.TemplateDelegate template) {
            if (it.SelectSection('{', '}')) {
                string key, type;

                if (it.SelectSection(" as ")) {
                    key = it;
                    it.ConsumeSection();
                    type = it;
                }
                else {
                    key = it;
                    type = "";
                }

				it.ConsumeSection();

				var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out Matcher.TemplateDelegate next_template);

                return parse_internal(
                    key,
                    type,
                    optional,
                    out go_to,
                    out extract,
                    out template,
                    has_next,
                    next_goto,
                    next_extract,
                    next_template
                );
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
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
			Matcher.TemplateDelegate next_template) 
        {
			if (it.SelectSection('{', '}')) {
				string key, type;

				if (it.SelectSection(" as ")) {
					key = it;
					it.ConsumeSection();
					type = it;
				}
				else {
					key = it;
					type = "";
				}

				it.ConsumeSection();

				var optional = it.Consume('?');

				var has_next = Parser.Parse(
					it,
					out ExtractDelegate parsed_goto,
					out ExtractDelegate parsed_extract,
					out Matcher.TemplateDelegate parsed_template,
					next_goto,
					next_extract,
					next_template);

				if (has_next) {
					return parse_internal(
						key,
						type,
						optional,
						out go_to,
						out extract,
						out template,
						true,
                        parsed_goto,
                        parsed_extract,
                        parsed_template
					);
                }
                else {
                    return parse_internal(
                        key,
                        type,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        next_goto,
                        next_extract,
                        next_template
                    );
                }
			}

			go_to = null;
			extract = null;
			template = null;
			return false;
		}
    }
}
