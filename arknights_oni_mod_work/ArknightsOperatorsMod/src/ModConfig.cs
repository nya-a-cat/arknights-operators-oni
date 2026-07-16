using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PeterHan.PLib.Options;
using UnityEngine;

namespace ArknightsOperatorsMod {
	[JsonConverter(typeof(StringEnumConverter))]
	public enum ResourcePersistencePolicy {
		[EnumMember(Value = "按需缓存（512 MiB）")]
		OnDemandCache = 0,

		[EnumMember(Value = "永久保留已下载资源")]
		Permanent = 1
	}

	[ConfigFile("config.json", true, true)]
	public sealed class ModConfig : IOptions {
		public const int CurrentSchemaVersion = 3;
		public const int MinimumCacheCapacityMiB = 128;
		public const int DefaultCacheCapacityMiB = 512;
		public const int MaximumCacheCapacityMiB = 2000;

		[JsonProperty]
		public int SchemaVersion { get; set; } = CurrentSchemaVersion;

		[JsonProperty]
		public ResourcePersistencePolicy DownloadPolicy { get; set; } =
			ResourcePersistencePolicy.OnDemandCache;

		[JsonProperty]
		public int CacheCapacityMiB { get; set; } = DefaultCacheCapacityMiB;

		[JsonProperty]
		public string DefaultCharacterId { get; set; } = "char_002_amiya";

		[JsonProperty]
		public string PreferredSkin { get; set; } = "默认";

		[JsonProperty]
		public string PreferredModel { get; set; } = "基建";

		[JsonProperty]
		public bool AutomaticModelSwitching { get; set; } = true;

		internal bool Normalize() {
			bool changed = false;
			if (SchemaVersion != CurrentSchemaVersion) {
				SchemaVersion = CurrentSchemaVersion;
				changed = true;
			}
			if (!Enum.IsDefined(typeof(ResourcePersistencePolicy), DownloadPolicy)) {
				DownloadPolicy = ResourcePersistencePolicy.OnDemandCache;
				changed = true;
			}
			if (!IsValidCacheCapacityMiB(CacheCapacityMiB)) {
				Debug.LogWarning("[ArknightsOperatorsMod] CacheCapacityMiB=" + CacheCapacityMiB +
					" is outside 128-2000; restored to 512 MiB");
				CacheCapacityMiB = DefaultCacheCapacityMiB;
				changed = true;
			}
			if (string.IsNullOrWhiteSpace(DefaultCharacterId)) {
				DefaultCharacterId = "char_002_amiya";
				changed = true;
			}
			if (string.IsNullOrWhiteSpace(PreferredSkin)) {
				PreferredSkin = "默认";
				changed = true;
			}
			if (string.IsNullOrWhiteSpace(PreferredModel)) {
				PreferredModel = "基建";
				changed = true;
			}
			return changed;
		}

		internal static bool IsValidCacheCapacityMiB(int capacityMiB) {
			return capacityMiB >= MinimumCacheCapacityMiB &&
				capacityMiB <= MaximumCacheCapacityMiB;
		}

		internal static long CacheCapacityBytes(int capacityMiB) {
			if (!IsValidCacheCapacityMiB(capacityMiB))
				capacityMiB = DefaultCacheCapacityMiB;
			return capacityMiB * 1024L * 1024L;
		}

		internal static bool IsCacheUsageOverTarget(long indexedBytes,
			ResourcePersistencePolicy policy, int capacityMiB) {
			return policy == ResourcePersistencePolicy.OnDemandCache &&
				indexedBytes > CacheCapacityBytes(capacityMiB);
		}

		internal static bool CanApplyCacheCapacityInput(ResourcePersistencePolicy policy,
			bool inputValid) {
			return policy == ResourcePersistencePolicy.Permanent || inputValid;
		}

		public IEnumerable<IOptionsEntry> CreateOptions() {
			yield return new OperatorAppearanceOptionsEntry(this);
		}

		public void OnOptionsChanged() {
			OperatorAppearanceOptionsEntry.ApplyPendingSelection(this);
			Normalize();
			ModConfigStore.SaveAndApply(this);
			Debug.Log("[ArknightsOperatorsMod] Saved appearance " + DefaultCharacterId + " " +
				PreferredSkin + "/" + PreferredModel + "; cache=" + CacheCapacityMiB + " MiB");
		}
	}

	public static class ModConfigStore {
		private static readonly object Gate = new object();
		private static string configPath;
		private static System.DateTime lastWriteUtc;
		private static ModConfig current;

		public static event Action<ModConfig> AppearanceChanged;

		public static string ConfigPath {
			get {
				EnsureInitialized();
				return configPath;
			}
		}

		public static ModConfig Current {
			get {
				lock (Gate) {
					EnsureInitializedNoLock();
					ReloadWhenChangedNoLock();
					return Clone(current);
				}
			}
		}

		public static ResourcePersistencePolicy DownloadPolicy {
			get { return Current.DownloadPolicy; }
		}

		public static int CacheCapacityMiB {
			get { return Current.CacheCapacityMiB; }
		}

		public static void Initialize() {
			lock (Gate) {
				EnsureInitializedNoLock();
			}
		}

		public static void ApplySaved(ModConfig saved) {
			Apply(saved, false);
		}

		public static void SaveAndApply(ModConfig saved) {
			Apply(saved, true);
		}

		private static void Apply(ModConfig saved, bool persist) {
			if (saved == null) throw new ArgumentNullException("saved");
			Action<ModConfig> changed = null;
			ModConfig snapshot = null;
			bool cacheSettingsChanged = false;
			lock (Gate) {
				EnsureInitializedNoLock();
				string previousAppearance = AppearanceKey(current);
				ResourcePersistencePolicy previousPolicy = current.DownloadPolicy;
				int previousCapacityMiB = current.CacheCapacityMiB;
				current = Clone(saved);
				current.Normalize();
				cacheSettingsChanged = previousPolicy != current.DownloadPolicy ||
					previousCapacityMiB != current.CacheCapacityMiB;
				if (persist)
					WriteNoLock(current);
				else
					lastWriteUtc = GetLastWriteUtcNoLock();
				if (!string.Equals(previousAppearance, AppearanceKey(current), StringComparison.Ordinal)) {
					changed = AppearanceChanged;
					snapshot = Clone(current);
				}
			}
			if (changed != null) changed(snapshot);
			if (cacheSettingsChanged && PrtsResourceService.Instance != null) {
				try {
					PrtsResourceService.Instance.RunCacheMaintenance();
				} catch (Exception error) {
					Debug.LogWarning("[ArknightsOperatorsMod] Cache maintenance after saving settings failed: " +
						error.Message);
				}
			}
		}

		internal static string AppearanceKey(ModConfig config) {
			if (config == null) return "";
			return config.DefaultCharacterId + "|" + config.PreferredSkin + "|" +
				config.PreferredModel + "|" + config.AutomaticModelSwitching;
		}

		internal static ModConfig Clone(ModConfig source) {
			return new ModConfig {
				SchemaVersion = source.SchemaVersion,
				DownloadPolicy = source.DownloadPolicy,
				CacheCapacityMiB = source.CacheCapacityMiB,
				DefaultCharacterId = source.DefaultCharacterId,
				PreferredSkin = source.PreferredSkin,
				PreferredModel = source.PreferredModel,
				AutomaticModelSwitching = source.AutomaticModelSwitching
			};
		}

		private static void EnsureInitialized() {
			lock (Gate) {
				EnsureInitializedNoLock();
			}
		}

		private static void EnsureInitializedNoLock() {
			if (current != null)
				return;
			configPath = POptions.GetConfigFilePath(typeof(ModConfig));
			current = ReadNoLock();
			if (current == null) {
				current = new ModConfig();
				WriteNoLock(current);
			}
			current.Normalize();
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static void ReloadWhenChangedNoLock() {
			System.DateTime actualWriteUtc = GetLastWriteUtcNoLock();
			if (actualWriteUtc == lastWriteUtc)
				return;
			ModConfig loaded = ReadNoLock();
			if (loaded != null) {
				loaded.Normalize();
				current = loaded;
			} else {
				current = new ModConfig();
				WriteNoLock(current);
			}
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static ModConfig ReadNoLock() {
			try {
				if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
					return null;
				ModConfig loaded = JsonConvert.DeserializeObject<ModConfig>(
					File.ReadAllText(configPath)
				);
				if (loaded != null && loaded.Normalize())
					WriteNoLock(loaded);
				return loaded;
			} catch (Exception error) {
				Debug.LogWarning("[ArknightsOperatorsMod] Failed to read config: " + error.Message);
				return null;
			}
		}

		private static void WriteNoLock(ModConfig config) {
			config.Normalize();
			string directory = Path.GetDirectoryName(configPath);
			Directory.CreateDirectory(directory);
			string partPath = configPath + ".part";
			File.WriteAllText(partPath, JsonConvert.SerializeObject(config, Formatting.Indented));
			AtomicFile.Replace(partPath, configPath);
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static System.DateTime GetLastWriteUtcNoLock() {
			return File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : System.DateTime.MinValue;
		}
	}
}
