using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AmiyaDuplicantMod {
	internal static class OperatorFallbackPackageInstaller {
		public const long MaximumPackageBytes = PrtsAssetClient.MaximumDownloadBytes;
		private static readonly SemaphoreSlim InstallGate = new SemaphoreSlim(1, 1);

		public static async Task InstallAsync(
			PrtsResourceService resources,
			OperatorFallbackPackage package,
			OperatorFallbackAppearance appearance,
			CancellationToken cancellationToken
		) {
			if (resources == null) throw new ArgumentNullException("resources");
			if (package == null) throw new ArgumentNullException("package");
			if (appearance == null) throw new ArgumentNullException("appearance");
			if (package.PackageLength > MaximumPackageBytes)
				throw new InvalidDataException("Fallback package exceeds the 512 MiB technical safety limit");

			string packageKey = "operator:" + package.CharacterId + ":fallback-package";
			string packageRelativePath = Path.Combine(
				"fallback-packages",
				package.CharacterId + "-" + appearance.ResourceVersion + ".zip"
			);
			PrtsAssetRequest packageRequest = new PrtsAssetRequest(
				packageKey,
				new Uri(package.PackageUrl),
				packageRelativePath,
				appearance.ResourceVersion,
				package.PackageLength,
				package.PackageSha256,
				MaximumPackageBytes
			);

			List<string> protectedKeys = new List<string> { packageKey };
			for (int i = 0; i < appearance.Files.Count; i++)
				protectedKeys.Add(OperatorAssetResolver.AssetKeyForFile(
					package.CharacterId,
					appearance,
					appearance.Files[i]
				));

			using (resources.Acquire(protectedKeys)) {
				string packagePath = await resources.GetOrDownloadAsync(
					packageRequest,
					cancellationToken
				).ConfigureAwait(false);
				await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
				try {
					InstallVerifiedFiles(resources, package, appearance, packagePath, cancellationToken);
				} finally {
					InstallGate.Release();
				}
			}
		}

		private static void InstallVerifiedFiles(
			PrtsResourceService resources,
			OperatorFallbackPackage package,
			OperatorFallbackAppearance appearance,
			string packagePath,
			CancellationToken cancellationToken
		) {
			List<StagedFile> staged = new List<StagedFile>();
			try {
				using (FileStream stream = File.OpenRead(packagePath))
				using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false)) {
					for (int i = 0; i < appearance.Files.Count; i++) {
						cancellationToken.ThrowIfCancellationRequested();
						OperatorFallbackFile file = appearance.Files[i];
						ZipArchiveEntry entry = archive.GetEntry(file.ArchivePath);
						if (entry == null)
							throw new InvalidDataException("Fallback package entry is missing: " + file.ArchivePath);
						if (entry.Length != file.Length || entry.Length > PrtsAssetClient.MaximumAssetBytes)
							throw new InvalidDataException("Fallback package entry length is invalid: " + file.ArchivePath);

						string stagedPath = Path.Combine(ModAssets.TempRoot, Guid.NewGuid().ToString("N") + ".part");
						string actualHash = ExtractAndHash(entry, stagedPath, cancellationToken);
						if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException("Fallback package entry SHA-256 is invalid: " + file.ArchivePath);
						staged.Add(new StagedFile(file, stagedPath));
					}
				}

				for (int i = 0; i < staged.Count; i++) {
					OperatorFallbackFile file = staged[i].File;
					PrtsAssetRequest request = new PrtsAssetRequest(
						OperatorAssetResolver.AssetKeyForFile(package.CharacterId, appearance, file),
						new Uri(file.SourceUrl),
						file.RelativePath,
						appearance.ResourceVersion,
						file.Length,
						file.Sha256
					);
					resources.CommitVerifiedFile(
						request,
						staged[i].Path,
						new Uri(package.PackageUrl),
						appearance.ResourceVersion
					);
				}
			} finally {
				for (int i = 0; i < staged.Count; i++) {
					if (File.Exists(staged[i].Path))
						File.Delete(staged[i].Path);
				}
			}
		}

		private static string ExtractAndHash(
			ZipArchiveEntry entry,
			string destination,
			CancellationToken cancellationToken
		) {
			byte[] buffer = new byte[64 * 1024];
			long written = 0L;
			using (Stream input = entry.Open())
			using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (SHA256 sha = SHA256.Create()) {
				while (true) {
					cancellationToken.ThrowIfCancellationRequested();
					int read = input.Read(buffer, 0, buffer.Length);
					if (read == 0)
						break;
					written += read;
					if (written > entry.Length || written > PrtsAssetClient.MaximumAssetBytes)
						throw new InvalidDataException("Fallback package entry expanded beyond its manifest length");
					sha.TransformBlock(buffer, 0, read, null, 0);
					output.Write(buffer, 0, read);
				}
				if (written != entry.Length)
					throw new InvalidDataException("Fallback package entry ended before its manifest length");
				sha.TransformFinalBlock(new byte[0], 0, 0);
				output.Flush();
				return BitConverter.ToString(sha.Hash).Replace("-", string.Empty);
			}
		}

		private sealed class StagedFile {
			public OperatorFallbackFile File { get; private set; }
			public string Path { get; private set; }

			public StagedFile(OperatorFallbackFile file, string path) {
				File = file;
				Path = path;
			}
		}
	}
}
