using System;
using System.IO;

using Poly.Data;

namespace Poly.Script.Helpers {
	using Nodes;

	public class PersistentFile {
		FileInfo Info;
		FileSystemWatcher Watcher;
		Variable Var;

		public PersistentFile (string FileName, Variable Var) {
			this.Info = new FileInfo (FileName);
			this.Var = Var;

			Watcher = new FileSystemWatcher (Info.Directory.FullName, Info.Name);

			Watcher.Changed += (object sender, FileSystemEventArgs e) => {
				Update();
			};
			Watcher.Created += (object sender, FileSystemEventArgs e) => {
				Update ();
			};

			Update ();

			Watcher.EnableRaisingEvents = true;
		}	

		private void Update() {
			try {
				if (Info.Exists) {
					var Content = File.ReadAllText(Info.FullName);

					if (Var.Assign(App.GlobalContext, new jsObject(Content))) {
						App.Log.Info("Persisting {0} : {1}", Info.FullName, DateTime.Now);
					}
				}
			}
			catch { Var.Assign (App.GlobalContext, null); }
		}
	}
}
