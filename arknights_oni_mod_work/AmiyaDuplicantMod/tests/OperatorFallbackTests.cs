using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AmiyaDuplicantMod;
using Newtonsoft.Json;

namespace UnityEngine {
	public static class Debug {
		public static void LogWarning(object value) { Console.Error.WriteLine(value); }
	}
}

namespace AmiyaDuplicantMod {
	public enum ResourcePersistencePolicy { OnDemandCache, Permanent }

	public sealed class ModConfig {
		public string DefaultCharacterId { get; set; }
		public string PreferredSkin { get; set; }
		public string PreferredModel { get; set; }
	}

	public static class ModConfigStore {
		public static ResourcePersistencePolicy DownloadPolicy {
			get { return ResourcePersistencePolicy.OnDemandCache; }
		}
	}

	public static class ModAssets {
		public static string SharedRoot;
		public static string SharedAssetsRoot { get { return Path.Combine(SharedRoot, "assets"); } }
		public static string TempRoot { get { return Path.Combine(SharedRoot, "tmp"); } }
		public static string CacheIndexPath { get { return Path.Combine(SharedRoot, "cache-index.json"); } }

		public static void InitializeSharedStorage() {
			Directory.CreateDirectory(SharedRoot);
			Directory.CreateDirectory(SharedAssetsRoot);
			Directory.CreateDirectory(TempRoot);
		}
	}
}

internal static class OperatorFallbackTests {
	private sealed class FakeHandler : HttpMessageHandler {
		private readonly Dictionary<string, byte[]> responses;
		public int Requests { get; private set; }

		public FakeHandler(Dictionary<string, byte[]> responses) {
			this.responses = responses;
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken
		) {
			Requests++;
			if (request.RequestUri.AbsolutePath.EndsWith("/timeout.bin", StringComparison.Ordinal))
				throw new TaskCanceledException("simulated source timeout");
			byte[] content;
			if (!responses.TryGetValue(request.RequestUri.AbsoluteUri, out content))
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) {
					RequestMessage = request
				});
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
				RequestMessage = request,
				Content = new ByteArrayContent(content)
			});
		}
	}

	private static void Require(bool condition, string message) {
		if (!condition) throw new InvalidOperationException(message);
	}

	private static string Sha256(byte[] bytes) {
		using (SHA256 sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
	}

	private static byte[] CreatePackage(IDictionary<string, byte[]> files) {
		using (MemoryStream output = new MemoryStream()) {
			using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, true)) {
				foreach (KeyValuePair<string, byte[]> file in files) {
					ZipArchiveEntry entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
					using (Stream stream = entry.Open())
						stream.Write(file.Value, 0, file.Value.Length);
				}
			}
			return output.ToArray();
		}
	}

	private static OperatorFallbackFile FileRecord(
		string role,
		string pageName,
		string path,
		byte[] content
	) {
		return new OperatorFallbackFile {
			Role = role,
			PageName = pageName,
			RelativePath = path,
			ArchivePath = path,
			SourceUrl = "https://torappu.prts.wiki/assets/char_spine/char_test/" + Path.GetFileName(path),
			Length = content.Length,
			Sha256 = Sha256(content)
		};
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated cache directory");
		ModAssets.SharedRoot = Path.GetFullPath(args[0]);

		byte[] atlas = Encoding.UTF8.GetBytes("texture.png\nsize: 8,8\nformat: RGBA8888\n\n");
		byte[] skeleton = Encoding.UTF8.GetBytes("test-skeleton");
		byte[] texture = Encoding.UTF8.GetBytes("test-texture");
		string root = "operators/char_test/default/build/";
		OperatorFallbackFile atlasFile = FileRecord("atlas", null, root + "test.atlas", atlas);
		OperatorFallbackFile skeletonFile = FileRecord("skel", null, root + "test.skel", skeleton);
		OperatorFallbackFile pageFile = FileRecord("page", "texture.png", root + "texture.png", texture);
		OperatorFallbackAppearance appearance = new OperatorFallbackAppearance {
			Skin = "默认",
			Model = "基建",
			ResourceVersion = "TEST-SNAPSHOT-1",
			Files = new List<OperatorFallbackFile> { atlasFile, skeletonFile, pageFile }
		};
		Dictionary<string, byte[]> packageFiles = new Dictionary<string, byte[]> {
			{ atlasFile.ArchivePath, atlas },
			{ skeletonFile.ArchivePath, skeleton },
			{ pageFile.ArchivePath, texture }
		};
		byte[] packageBytes = CreatePackage(packageFiles);
		string packageUrl =
			"https://github.com/nya-a-cat/arknights-oni/releases/download/assets-v1.0.0/operator-char_test.zip";
		OperatorFallbackPackage package = new OperatorFallbackPackage {
			CharacterId = "char_test",
			CharacterName = "测试干员",
			PackageUrl = packageUrl,
			PackageLength = packageBytes.Length,
			PackageSha256 = Sha256(packageBytes),
			Appearances = new List<OperatorFallbackAppearance> { appearance }
		};
		OperatorAssetFallbackManifest manifest = new OperatorAssetFallbackManifest {
			SchemaVersion = OperatorAssetFallbackManifest.CurrentSchemaVersion,
			SnapshotId = "fixture-1",
			ReleaseTag = "assets-v1.0.0",
			Operators = new List<OperatorFallbackPackage> { package }
		};
		OperatorAssetFallbackManifest parsed = OperatorAssetFallbackManifest.Parse(
			JsonConvert.SerializeObject(manifest)
		);
		OperatorFallbackPackage chosenPackage;
		OperatorFallbackAppearance chosen = parsed.Choose("char_test", "默认", "基建", out chosenPackage);
		Require(chosen != null && object.ReferenceEquals(chosenPackage, parsed.Operators[0]),
			"manifest selection failed");

		byte[] correct = Encoding.UTF8.GetBytes("verified-fallback");
		string directFallbackUrl =
			"https://github.com/nya-a-cat/arknights-oni/releases/download/assets-v1.0.0/fallback.bin";
		FakeHandler handler = new FakeHandler(new Dictionary<string, byte[]> {
			{ directFallbackUrl, correct },
			{ packageUrl, packageBytes }
		});
		PrtsAssetClient client = new PrtsAssetClient(handler);
		string directPart = Path.Combine(ModAssets.SharedRoot, "direct.part");
		PrtsDownloadResult directResult = client.DownloadAsync(
			new PrtsAssetRequest(
				"direct-fallback",
				new List<PrtsAssetSource> {
					new PrtsAssetSource(
						new Uri("https://torappu.prts.wiki/assets/timeout.bin"),
						correct.Length,
						Sha256(correct),
						"fixture-1"
					),
					new PrtsAssetSource(
						new Uri(directFallbackUrl),
						correct.Length,
						Sha256(correct),
						"fixture-1"
					)
				},
				"direct.bin",
				"fixture-1"
			),
			directPart,
			CancellationToken.None
		).GetAwaiter().GetResult();
		Require(directResult.SourceUri.AbsoluteUri == directFallbackUrl, "secondary source was not used");
		Require(File.ReadAllBytes(directPart).Length == correct.Length, "secondary content was not stored");
		File.Delete(directPart);

		PrtsResourceService.InitializeForTests(client);
		try {
			OperatorFallbackPackageInstaller.InstallAsync(
				PrtsResourceService.Instance,
				chosenPackage,
				chosen,
				CancellationToken.None
			).GetAwaiter().GetResult();
			foreach (OperatorFallbackFile file in chosen.Files) {
				PrtsAssetRequest request = new PrtsAssetRequest(
					OperatorAssetResolver.AssetKeyForFile(chosenPackage.CharacterId, chosen, file),
					new Uri(file.SourceUrl),
					file.RelativePath,
					chosen.ResourceVersion,
					file.Length,
					file.Sha256
				);
				string cachedPath;
				Require(PrtsResourceService.Instance.TryGetOffline(request, out cachedPath),
					"installed package file was not indexed: " + file.Role);
				Require(Sha256(File.ReadAllBytes(cachedPath)) == file.Sha256,
					"installed package file hash mismatch: " + file.Role);
			}
		} finally {
			PrtsResourceService.Shutdown();
		}

		Require(handler.Requests >= 3, "fallback test did not exercise both source paths");
		Console.WriteLine("OperatorFallbackTests: passed manifest, source fallback, package extraction and cache indexing");
		return 0;
	}
}
