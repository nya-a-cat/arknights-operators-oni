using System.IO;

namespace ArknightsOperatorsMod {
	internal static class AtomicFile {
		public static void Replace(string partPath, string destinationPath) {
			if (File.Exists(destinationPath))
				File.Replace(partPath, destinationPath, null);
			else
				File.Move(partPath, destinationPath);
		}
	}
}
