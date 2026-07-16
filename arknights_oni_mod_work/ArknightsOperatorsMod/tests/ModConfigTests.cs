using System;
using System.Collections.Generic;
using System.IO;
using ArknightsOperatorsMod;
using Newtonsoft.Json;

namespace UnityEngine {
	public static class Debug {
		public static int WarningCount { get; private set; }

		public static void Log(object value) { }
		public static void LogWarning(object value) {
			WarningCount++;
			Console.Error.WriteLine(value);
		}

		public static void ResetWarnings() {
			WarningCount = 0;
		}
	}
}

namespace PeterHan.PLib.Options {
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class ConfigFileAttribute : Attribute {
		public ConfigFileAttribute(string fileName, bool shared, bool indent) { }
	}

	public interface IOptions {
		IEnumerable<IOptionsEntry> CreateOptions();
		void OnOptionsChanged();
	}

	public interface IOptionsEntry { }

	public static class POptions {
		public static string ConfigFilePath;

		public static string GetConfigFilePath(Type type) {
			return ConfigFilePath;
		}
	}
}

namespace ArknightsOperatorsMod {
	public sealed class OperatorAppearanceOptionsEntry : PeterHan.PLib.Options.IOptionsEntry {
		public OperatorAppearanceOptionsEntry(ModConfig config) { }

		public static bool ApplyPendingSelection(ModConfig config) {
			return false;
		}
	}

	public sealed class PrtsResourceService {
		public static PrtsResourceService Instance { get; set; }
		public int MaintenanceRuns { get; private set; }

		public long RunCacheMaintenance() {
			MaintenanceRuns++;
			return 0L;
		}
	}
}

internal static class ModConfigTests {
	private static int assertions;

	private static void Require(bool condition, string message) {
		assertions++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private static ModConfig Normalize(int capacityMiB) {
		ModConfig config = new ModConfig { CacheCapacityMiB = capacityMiB };
		config.Normalize();
		return config;
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated config directory");
		string root = Path.GetFullPath(args[0]);
		Directory.CreateDirectory(root);
		PeterHan.PLib.Options.POptions.ConfigFilePath = Path.Combine(root, "config.json");

		ModConfig defaults = new ModConfig();
		Require(defaults.SchemaVersion == ModConfig.CurrentSchemaVersion,
			"new config schema version is incorrect");
		Require(defaults.CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"new config cache capacity is not 512 MiB");

		ModConfig legacy = JsonConvert.DeserializeObject<ModConfig>("{\"SchemaVersion\":2}");
		legacy.Normalize();
		Require(legacy.CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"legacy config without CacheCapacityMiB did not migrate to 512 MiB");
		Require(legacy.SchemaVersion == ModConfig.CurrentSchemaVersion,
			"legacy config schema version was not upgraded");

		UnityEngine.Debug.ResetWarnings();
		Require(Normalize(127).CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"capacity below 128 MiB did not fall back to 512 MiB");
		Require(Normalize(128).CacheCapacityMiB == 128,
			"minimum cache capacity was rejected");
		Require(Normalize(512).CacheCapacityMiB == 512,
			"default cache capacity was changed");
		Require(Normalize(2000).CacheCapacityMiB == 2000,
			"maximum cache capacity was rejected");
		Require(Normalize(2001).CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"capacity above 2000 MiB did not fall back to 512 MiB");
		Require(UnityEngine.Debug.WarningCount == 2,
			"out-of-range cache values did not log warnings");
		Require(ModConfig.IsValidCacheCapacityMiB(128) &&
			ModConfig.IsValidCacheCapacityMiB(2000) &&
			!ModConfig.IsValidCacheCapacityMiB(127) &&
			!ModConfig.IsValidCacheCapacityMiB(2001),
			"cache capacity boundary validation is incorrect");
		long target128 = 128L * 1024L * 1024L;
		Require(!ModConfig.IsCacheUsageOverTarget(target128,
			ResourcePersistencePolicy.OnDemandCache, 128),
			"usage equal to the target was reported as over target");
		Require(ModConfig.IsCacheUsageOverTarget(target128 + 1L,
			ResourcePersistencePolicy.OnDemandCache, 128),
			"on-demand usage above the target was not detected");
		Require(!ModConfig.IsCacheUsageOverTarget(target128 + 1L,
			ResourcePersistencePolicy.Permanent, 128),
			"permanent retention was incorrectly reported as over target");
		Require(!ModConfig.CanApplyCacheCapacityInput(
			ResourcePersistencePolicy.OnDemandCache, false),
			"on-demand mode accepted invalid cache capacity input");
		Require(ModConfig.CanApplyCacheCapacityInput(
			ResourcePersistencePolicy.OnDemandCache, true),
			"on-demand mode rejected valid cache capacity input");
		Require(ModConfig.CanApplyCacheCapacityInput(ResourcePersistencePolicy.Permanent, false),
			"permanent mode did not preserve the previous valid capacity");

		ModConfig clone = ModConfigStore.Clone(new ModConfig { CacheCapacityMiB = 731 });
		Require(clone.CacheCapacityMiB == 731, "config clone lost cache capacity");

		PrtsResourceService service = new PrtsResourceService();
		PrtsResourceService.Instance = service;
		File.WriteAllText(PeterHan.PLib.Options.POptions.ConfigFilePath, "{broken-json");
		UnityEngine.Debug.ResetWarnings();
		ModConfigStore.Initialize();
		Require(UnityEngine.Debug.WarningCount == 1,
			"malformed config JSON did not log a warning");
		Require(File.ReadAllText(PeterHan.PLib.Options.POptions.ConfigFilePath).Contains(
			"\"CacheCapacityMiB\": 512"),
			"malformed config JSON was not replaced with defaults");
		ModConfigStore.SaveAndApply(new ModConfig { CacheCapacityMiB = 128 });
		Require(ModConfigStore.CacheCapacityMiB == 128,
			"saved cache capacity was not applied");
		Require(service.MaintenanceRuns == 1,
			"changing cache capacity did not run maintenance");

		ModConfigStore.SaveAndApply(new ModConfig { CacheCapacityMiB = 128 });
		Require(service.MaintenanceRuns == 1,
			"unchanged cache settings ran redundant maintenance");

		ModConfig permanent = ModConfigStore.Current;
		permanent.DownloadPolicy = ResourcePersistencePolicy.Permanent;
		ModConfigStore.SaveAndApply(permanent);
		Require(service.MaintenanceRuns == 2,
			"changing resource persistence policy did not run maintenance");
		Require(ModConfigStore.Current.CacheCapacityMiB == 128,
			"permanent policy discarded the configured capacity");

		string persisted = File.ReadAllText(PeterHan.PLib.Options.POptions.ConfigFilePath);
		Require(persisted.Contains("\"CacheCapacityMiB\": 128"),
			"persisted config does not contain cache capacity");

		ModConfig invalidDiskConfig = new ModConfig {
			DownloadPolicy = ResourcePersistencePolicy.OnDemandCache,
			CacheCapacityMiB = 50,
			DefaultCharacterId = "char_disk_repair"
		};
		File.WriteAllText(PeterHan.PLib.Options.POptions.ConfigFilePath,
			JsonConvert.SerializeObject(invalidDiskConfig, Formatting.Indented));
		File.SetLastWriteTimeUtc(PeterHan.PLib.Options.POptions.ConfigFilePath,
			DateTime.UtcNow.AddSeconds(2));
		UnityEngine.Debug.ResetWarnings();
		ModConfig repaired = ModConfigStore.Current;
		Require(repaired.CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"out-of-range disk capacity was not restored to 512 MiB");
		Require(repaired.DefaultCharacterId == "char_disk_repair",
			"repairing disk capacity discarded unrelated config values");
		Require(UnityEngine.Debug.WarningCount == 1,
			"repairing out-of-range disk capacity did not log one warning");
		string repairedJson = File.ReadAllText(PeterHan.PLib.Options.POptions.ConfigFilePath);
		Require(repairedJson.Contains("\"CacheCapacityMiB\": 512"),
			"out-of-range disk capacity was not immediately rewritten");

		File.Delete(PeterHan.PLib.Options.POptions.ConfigFilePath);
		ModConfig restoredAfterDelete = ModConfigStore.Current;
		Require(restoredAfterDelete.CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"runtime config deletion did not restore the default cache capacity");
		Require(restoredAfterDelete.DefaultCharacterId == "char_002_amiya",
			"runtime config deletion kept stale in-memory appearance settings");
		Require(File.Exists(PeterHan.PLib.Options.POptions.ConfigFilePath) &&
			File.ReadAllText(PeterHan.PLib.Options.POptions.ConfigFilePath).Contains(
				"\"CacheCapacityMiB\": 512"),
			"runtime config deletion did not immediately recreate the defaults file");

		File.WriteAllText(PeterHan.PLib.Options.POptions.ConfigFilePath, "{runtime-broken-json");
		File.SetLastWriteTimeUtc(PeterHan.PLib.Options.POptions.ConfigFilePath,
			DateTime.UtcNow.AddSeconds(2));
		UnityEngine.Debug.ResetWarnings();
		ModConfig restoredAfterCorruption = ModConfigStore.Current;
		Require(restoredAfterCorruption.CacheCapacityMiB == ModConfig.DefaultCacheCapacityMiB,
			"runtime config corruption did not restore the default cache capacity");
		Require(UnityEngine.Debug.WarningCount == 1,
			"runtime config corruption did not log one warning");
		Require(File.ReadAllText(PeterHan.PLib.Options.POptions.ConfigFilePath).Contains(
			"\"CacheCapacityMiB\": 512"),
			"runtime config corruption did not immediately rewrite the defaults file");

		Console.WriteLine("ModConfigTests: " + assertions +
			" passed defaults, migration, boundaries, persistence and maintenance");
		return 0;
	}
}
