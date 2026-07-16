using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArknightsOperatorsMod;
using Newtonsoft.Json;

namespace UnityEngine {
	public static class Debug {
		public static void LogWarning(object value) { Console.Error.WriteLine(value); }
	}
}

namespace ArknightsOperatorsMod {
	public enum ResourcePersistencePolicy { OnDemandCache, Permanent }

	public sealed class ModConfig {
		public const int MinimumCacheCapacityMiB = 128;
		public const int DefaultCacheCapacityMiB = 512;
		public const int MaximumCacheCapacityMiB = 2000;

		public ResourcePersistencePolicy DownloadPolicy { get; set; }
		public int CacheCapacityMiB { get; set; }
		public string DefaultCharacterId { get; set; }
		public string PreferredSkin { get; set; }
		public string PreferredModel { get; set; }

		public static long CacheCapacityBytes(int capacityMiB) {
			if (capacityMiB < MinimumCacheCapacityMiB || capacityMiB > MaximumCacheCapacityMiB)
				capacityMiB = DefaultCacheCapacityMiB;
			return capacityMiB * 1024L * 1024L;
		}
	}

	public static class ModConfigStore {
		public static ResourcePersistencePolicy Policy = ResourcePersistencePolicy.OnDemandCache;
		public static int CapacityMiB = ModConfig.DefaultCacheCapacityMiB;

		public static ResourcePersistencePolicy DownloadPolicy {
			get { return Policy; }
		}

		public static ModConfig Current {
			get {
				return new ModConfig {
					DownloadPolicy = Policy,
					CacheCapacityMiB = CapacityMiB
				};
			}
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
	private sealed class CountingLease : IDisposable {
		private int disposed;

		public int DisposeCount { get; private set; }

		public void Dispose() {
			if (Interlocked.Exchange(ref disposed, 1) != 0) return;
			DisposeCount++;
		}
	}

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

	private static void SeedSparseCacheEntry(
		ResourceIndexStore store,
		string key,
		long length,
		System.DateTime lastAccessUtc
	) {
		string relativePath = Path.Combine("seed", key + ".bin");
		string path = Path.Combine(ModAssets.SharedAssetsRoot, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
			stream.SetLength(length);
		store.Upsert(new ResourceIndexEntry {
			Key = key,
			RelativePath = relativePath,
			SourceUrl = "https://example.invalid/" + key,
			ResourceVersion = "cache-test",
			Length = length,
			Sha256 = "",
			LastAccessUtc = lastAccessUtc
		});
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

	private static void RequireBundleFiles(OperatorAssetBundle bundle, string source) {
		Require(bundle != null, source + " did not return a bundle");
		Require(File.Exists(bundle.AtlasPath), source + " atlas was evicted before use");
		Require(File.Exists(bundle.SkeletonPath), source + " skeleton was evicted before use");
		Require(bundle.TexturePaths.Count == 2, source + " did not resolve both atlas pages");
		for (int i = 0; i < bundle.TexturePaths.Count; i++)
			Require(File.Exists(bundle.TexturePaths[i]),
				source + " texture page was evicted before use: " + i);
	}

	private static void RequireAcquireTrimCoordination(PrtsResourceService service) {
		FieldInfo indexField = typeof(PrtsResourceService).GetField("index",
			BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo activeGateField = typeof(PrtsResourceService).GetField("activeGate",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Require(indexField != null && activeGateField != null,
			"cache coordination fields were not found");
		object index = indexField.GetValue(service);
		FieldInfo indexGateField = index.GetType().GetField("gate",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Require(indexGateField != null, "resource index gate was not found");
		object indexGate = indexGateField.GetValue(index);
		object activeGate = activeGateField.GetValue(service);
		Task<long> maintenance = null;
		Task<IDisposable> acquire = null;
		Monitor.Enter(indexGate);
		try {
			maintenance = Task.Run(() => service.RunCacheMaintenance());
			bool maintenanceOwnsActiveGate = false;
			DateTime deadline = DateTime.UtcNow.AddSeconds(3);
			while (DateTime.UtcNow < deadline) {
				if (!Monitor.TryEnter(activeGate)) {
					maintenanceOwnsActiveGate = true;
					break;
				}
				Monitor.Exit(activeGate);
				Thread.Sleep(5);
			}
			Require(maintenanceOwnsActiveGate,
				"cache maintenance did not hold the active lease gate through trimming");
			acquire = Task.Run(() => service.Acquire(new[] { "concurrent-acquire" }));
			Thread.Sleep(50);
			Require(!acquire.IsCompleted,
				"Acquire raced between the protected-key snapshot and LRU trimming");
		} finally {
			Monitor.Exit(indexGate);
		}
		if (maintenance != null) maintenance.GetAwaiter().GetResult();
		if (acquire != null) acquire.GetAwaiter().GetResult().Dispose();
	}

	private static void RequireBundleLifecycleCleanup() {
		CountingLease abandonedLease = new CountingLease();
		OperatorAssetBundle abandonedBundle = new OperatorAssetBundle(
			"char_abandoned", "Abandoned", "skin", "model", "v1",
			"atlas", "skeleton", new List<string>(), new List<string>(), abandonedLease);
		TaskCompletionSource<OperatorAssetBundle> pending =
			new TaskCompletionSource<OperatorAssetBundle>();
		Task cleanup = OperatorAssetBundleLifecycle.DisposeWhenCompleteAsync(pending.Task);
		Require(abandonedLease.DisposeCount == 0,
			"pending abandoned bundle was disposed before its task completed");
		pending.SetResult(abandonedBundle);
		cleanup.GetAwaiter().GetResult();
		Require(abandonedLease.DisposeCount == 1,
			"completed abandoned bundle did not release its lease");
		abandonedBundle.Dispose();
		Require(abandonedLease.DisposeCount == 1,
			"abandoned bundle cleanup was not idempotent");

		CountingLease failedApplyLease = new CountingLease();
		OperatorAssetBundle failedApplyBundle = new OperatorAssetBundle(
			"char_failed", "Failed", "skin", "model", "v1",
			"atlas", "skeleton", new List<string>(), new List<string>(), failedApplyLease);
		IDisposable transferred = failedApplyBundle.TakeResourceLease();
		try {
			throw new InvalidOperationException("simulated visual apply failure");
		} catch (InvalidOperationException) {
		} finally {
			if (transferred != null) transferred.Dispose();
			failedApplyBundle.Dispose();
		}
		Require(failedApplyLease.DisposeCount == 1,
			"failed visual apply did not release the transferred lease exactly once");

		TaskCompletionSource<OperatorAssetBundle> faulted =
			new TaskCompletionSource<OperatorAssetBundle>();
		Task faultCleanup = OperatorAssetBundleLifecycle.DisposeWhenCompleteAsync(faulted.Task);
		faulted.SetException(new IOException("simulated resolver failure"));
		faultCleanup.GetAwaiter().GetResult();
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated cache directory");
		ModAssets.SharedRoot = Path.GetFullPath(args[0]);
		RequireBundleLifecycleCleanup();

		byte[] atlas = Encoding.UTF8.GetBytes(
			"texture.png\nsize: 8,8\nformat: RGBA8888\n\n" +
			"texture-2.png\nsize: 8,8\nformat: RGBA8888\n\n");
		byte[] skeleton = Encoding.UTF8.GetBytes("test-skeleton");
		byte[] texture = Encoding.UTF8.GetBytes("test-texture");
		byte[] texture2 = Encoding.UTF8.GetBytes("test-texture-2");
		string root = "operators/char_test/default/build/";
		OperatorFallbackFile atlasFile = FileRecord("atlas", null, root + "test.atlas", atlas);
		OperatorFallbackFile skeletonFile = FileRecord("skel", null, root + "test.skel", skeleton);
		OperatorFallbackFile pageFile = FileRecord("page", "texture.png", root + "texture.png", texture);
		OperatorFallbackFile pageFile2 = FileRecord("page", "texture-2.png",
			root + "texture-2.png", texture2);
		OperatorFallbackAppearance appearance = new OperatorFallbackAppearance {
			Skin = "默认",
			Model = "基建",
			ResourceVersion = "TEST-SNAPSHOT-1",
			Files = new List<OperatorFallbackFile> {
				atlasFile, skeletonFile, pageFile, pageFile2
			}
		};
		Dictionary<string, byte[]> packageFiles = new Dictionary<string, byte[]> {
			{ atlasFile.ArchivePath, atlas },
			{ skeletonFile.ArchivePath, skeleton },
			{ pageFile.ArchivePath, texture },
			{ pageFile2.ArchivePath, texture2 }
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
		const string fallbackManifestUrl =
			"https://github.com/nya-a-cat/arknights-oni/releases/download/assets-v1.0.0/" +
			"operator-asset-fallback-manifest-v1.json";
		const string directRoot = "https://torappu.prts.wiki/assets/char_spine/char_direct/";
		byte[] directAtlas = Encoding.UTF8.GetBytes(
			"direct-a.png\nsize: 8,8\nformat: RGBA8888\n\n" +
			"direct-b.png\nsize: 8,8\nformat: RGBA8888\n\n");
		Dictionary<string, object> directModel = new Dictionary<string, object> {
			{ "file", "direct" }
		};
		Dictionary<string, object> directSkin = new Dictionary<string, object> {
			{ chosen.Model, directModel }
		};
		Dictionary<string, object> directSkins = new Dictionary<string, object> {
			{ chosen.Skin, directSkin }
		};
		string directMetaJson = JsonConvert.SerializeObject(new Dictionary<string, object> {
			{ "name", "Direct Operator" },
			{ "prefix", directRoot },
			{ "skin", directSkins }
		});
		Dictionary<string, byte[]> responses = new Dictionary<string, byte[]> {
			{ directFallbackUrl, correct },
			{ packageUrl, packageBytes },
			{ fallbackManifestUrl, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest)) },
			{ directRoot + "meta.json", Encoding.UTF8.GetBytes(directMetaJson) },
			{ directRoot + "direct.atlas", directAtlas },
			{ directRoot + "direct.skel", Encoding.UTF8.GetBytes("direct-skeleton") },
			{ directRoot + "direct-a.png", Encoding.UTF8.GetBytes("direct-texture-a") },
			{ directRoot + "direct-b.png", Encoding.UTF8.GetBytes("direct-texture-b") }
		};
		responses[atlasFile.SourceUrl] = atlas;
		responses[skeletonFile.SourceUrl] = skeleton;
		responses[pageFile.SourceUrl] = texture;
		responses[pageFile2.SourceUrl] = texture2;
		FakeHandler handler = new FakeHandler(responses);
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

		Require(PrtsResourceService.CacheLimitBytesForMiB(128) == 128L * 1024L * 1024L,
			"minimum cache capacity conversion failed");
		Require(PrtsResourceService.CacheLimitBytesForMiB(2000) == 2000L * 1024L * 1024L,
			"maximum cache capacity conversion failed");
		Require(PrtsResourceService.CacheLimitBytesForMiB(127) == 512L * 1024L * 1024L,
			"invalid cache capacity did not fall back to 512 MiB");

		ModAssets.InitializeSharedStorage();
		ResourceIndexStore seededIndex = new ResourceIndexStore(
			ModAssets.CacheIndexPath,
			ModAssets.SharedAssetsRoot
		);
		long sparseLength = 70L * 1024L * 1024L;
		long backgroundLength = 128L * 1024L * 1024L;
		System.DateTime seedTime = System.DateTime.UtcNow.AddDays(-1);
		SeedSparseCacheEntry(seededIndex, "leased-a", sparseLength, seedTime);
		SeedSparseCacheEntry(seededIndex, "leased-b", sparseLength, seedTime.AddMinutes(1));
		SeedSparseCacheEntry(seededIndex, "unleased", sparseLength, seedTime.AddMinutes(2));
		SeedSparseCacheEntry(seededIndex, "background", backgroundLength, seedTime.AddMinutes(3));
		ModConfigStore.Policy = ResourcePersistencePolicy.OnDemandCache;
		ModConfigStore.CapacityMiB = 128;

		PrtsResourceService.InitializeForTests(client);
		IDisposable backgroundLease = null;
		try {
			backgroundLease = PrtsResourceService.Instance.Acquire(new[] { "background" });
			IDisposable lease = PrtsResourceService.Instance.Acquire(new[] { "leased-a", "leased-b" });
			long protectedUsage = PrtsResourceService.Instance.RunCacheMaintenance();
			Require(protectedUsage == backgroundLength + sparseLength * 2,
				"cache maintenance did not retain all leased resources");
			Require(File.Exists(Path.Combine(ModAssets.SharedAssetsRoot, "seed", "leased-a.bin")) &&
				File.Exists(Path.Combine(ModAssets.SharedAssetsRoot, "seed", "leased-b.bin")),
				"cache maintenance removed an active lease");
			Require(!File.Exists(Path.Combine(ModAssets.SharedAssetsRoot, "seed", "unleased.bin")),
				"cache maintenance did not remove the unleased LRU candidate");
			lease.Dispose();
			Require(PrtsResourceService.Instance.IndexedDiskUsage == backgroundLength,
				"releasing leases did not resume cache maintenance");

			List<string> appearanceKeys = new List<string>();
			for (int i = 0; i < chosen.Files.Count; i++)
				appearanceKeys.Add(OperatorAssetResolver.AssetKeyForFile(
					chosenPackage.CharacterId, chosen, chosen.Files[i]));
			using (PrtsResourceService.Instance.Acquire(appearanceKeys)) {
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
			}

			OperatorAssetResolver resolver = new OperatorAssetResolver(PrtsResourceService.Instance);
			OperatorAssetBundle directBundle = resolver.ResolveAsync(new ModConfig {
				DefaultCharacterId = "char_direct",
				PreferredSkin = chosen.Skin,
				PreferredModel = chosen.Model
			}, CancellationToken.None).GetAwaiter().GetResult();
			try {
				RequireBundleFiles(directBundle, "direct PRTS resolution");
			} finally {
				directBundle.Dispose();
			}

			OperatorAssetBundle fallbackBundle = resolver.ResolveAsync(new ModConfig {
				DefaultCharacterId = "char_test",
				PreferredSkin = chosen.Skin,
				PreferredModel = chosen.Model
			}, CancellationToken.None).GetAwaiter().GetResult();
			try {
				RequireBundleFiles(fallbackBundle, "GitHub fallback resolution");
			} finally {
				fallbackBundle.Dispose();
			}

			RequireAcquireTrimCoordination(PrtsResourceService.Instance);
			IDisposable concurrentDisposeLease = PrtsResourceService.Instance.Acquire(
				new[] { "background" });
			Task.WaitAll(
				Task.Run(() => concurrentDisposeLease.Dispose()),
				Task.Run(() => concurrentDisposeLease.Dispose())
			);
			Require(File.Exists(Path.Combine(ModAssets.SharedAssetsRoot, "seed", "background.bin")),
				"concurrent Dispose released another holder's active lease");
		} finally {
			if (backgroundLease != null) backgroundLease.Dispose();
			PrtsResourceService.Shutdown();
		}

		Require(handler.Requests >= 12, "fallback test did not exercise all source paths");
		Console.WriteLine("OperatorFallbackTests: passed direct/fallback multi-page resolution, " +
			"cache capacity, atomic leases and indexing");
		return 0;
	}
}
