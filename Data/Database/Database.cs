using System;
using System.Collections.Generic;

namespace Poly.Data {
	public partial class Database {
		public KeyValueCollection<Group> Groups;

		public Database() {
			Groups = new KeyValueCollection<Group>();
		}

		public Group this[string key] {
			get {
				return Groups[key];
			}
			set {
				Groups[key] = value;
			}
		}
	}
}