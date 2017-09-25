using System;

namespace Poly.Data {
	public partial class Database {
		public interface Group	{ 
			string Name { get; }
			int Count { get; }
		}

		public struct Group<DocType> : Group {
			public ManagedArray<DocType> Documents;

			public string Name { get; }
			public int Count { get { return Documents.Count; }}

			public Group(string name) {
				Name = name;
				Documents = new ManagedArray<DocType>();
			}
		}
	}
}