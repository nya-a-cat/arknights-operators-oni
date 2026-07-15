using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class OperatorAssetBundle {
		public string CharacterId { get; private set; }
		public string CharacterName { get; private set; }
		public string Skin { get; private set; }
		public string Model { get; private set; }
		public string ResourceVersion { get; private set; }
		public string AtlasPath { get; private set; }
		public string SkeletonPath { get; private set; }
		public IList<string> TexturePaths { get; private set; }
		public IList<string> ResourceKeys { get; private set; }

		internal OperatorAssetBundle(string characterId, string characterName, string skin, string model,
			string resourceVersion, string atlasPath, string skeletonPath, IList<string> texturePaths,
			IList<string> resourceKeys) {
			CharacterId = characterId;
			CharacterName = characterName;
			Skin = skin;
			Model = model;
			ResourceVersion = resourceVersion;
			AtlasPath = atlasPath;
			SkeletonPath = skeletonPath;
			TexturePaths = texturePaths;
			ResourceKeys = resourceKeys;
		}
	}

	public sealed class OperatorAssetResolver {
		private const string AssetRoot = "https://torappu.prts.wiki/assets/char_spine/";
		private const string FallbackReleaseTag = "assets-v1.0.0";
		private const string FallbackManifestName = "operator-asset-fallback-manifest-v1.json";
		private const string FallbackManifestUrl =
			"https://github.com/nya-a-cat/arknights-oni/releases/download/" +
			FallbackReleaseTag + "/" + FallbackManifestName;
		private readonly PrtsResourceService resources;

		public OperatorAssetResolver(PrtsResourceService resources) {
			this.resources = resources ?? throw new ArgumentNullException("resources");
		}

		public async Task<OperatorAssetBundle> ResolveAsync(ModConfig config, CancellationToken cancellationToken) {
			if (config == null) throw new ArgumentNullException("config");
			string characterId = ValidatePathToken(config.DefaultCharacterId, "character ID");
			Exception primaryError;
			try {
				return await ResolveFromPrtsAsync(config, characterId, cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception error) {
				primaryError = error;
			}

			try {
				OperatorAssetFallbackManifest manifest = await LoadFallbackManifestAsync(cancellationToken)
					.ConfigureAwait(false);
				OperatorFallbackPackage package;
				OperatorFallbackAppearance appearance = manifest.Choose(
					characterId,
					config.PreferredSkin,
					config.PreferredModel,
					out package
				);
				if (appearance == null)
					throw new InvalidDataException("Fallback snapshot has no entry for " + characterId);

				Debug.LogWarning(
					"[ArknightsOperatorsMod] PRTS resolution failed; using fallback snapshot " +
					manifest.SnapshotId + ": " + primaryError.Message
				);
				try {
					return await ResolveFromFallbackFilesAsync(
						package,
						appearance,
						cancellationToken
					).ConfigureAwait(false);
				} catch (OperationCanceledException) {
					throw;
				} catch (Exception directError) {
					Debug.LogWarning(
						"[ArknightsOperatorsMod] PRTS snapshot files failed; installing the Release package: " +
						directError.Message
					);
					await OperatorFallbackPackageInstaller.InstallAsync(
						resources,
						package,
						appearance,
						cancellationToken
					).ConfigureAwait(false);
					return await ResolveFromFallbackFilesAsync(
						package,
						appearance,
						cancellationToken
					).ConfigureAwait(false);
				}
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception fallbackError) {
				throw new AggregateException(
					"Operator assets failed from PRTS and the GitHub Release fallback",
					primaryError,
					fallbackError
				);
			}
		}

		private async Task<OperatorAssetBundle> ResolveFromPrtsAsync(
			ModConfig config,
			string characterId,
			CancellationToken cancellationToken
		) {
			Uri metaUri = new Uri(AssetRoot + Uri.EscapeDataString(characterId) + "/meta.json");
			string metaRelativePath = Path.Combine("operators", characterId, "meta.json");
			string refreshBucket = System.DateTime.UtcNow.ToString("yyyyMMdd");
			PrtsAssetRequest metaRequest = new PrtsAssetRequest(
				"operator:" + characterId + ":meta",
				metaUri,
				metaRelativePath,
				refreshBucket
			);
			string metaPath = await resources.GetOrDownloadAsync(metaRequest, cancellationToken).ConfigureAwait(false);
			string metaText = File.ReadAllText(metaPath);
			JObject meta = JObject.Parse(metaText);
			string resourceVersion = ComputeSha256(metaText).Substring(0, 16);

			JObject skins = meta["skin"] as JObject;
			if (skins == null || !skins.HasValues) throw new InvalidDataException("PRTS metadata has no skins");
			JProperty skin = ChooseProperty(skins, config.PreferredSkin, "默认");
			JObject models = skin.Value as JObject;
			if (models == null || !models.HasValues) throw new InvalidDataException("PRTS skin has no models: " + skin.Name);
			JProperty model = ChooseProperty(models, config.PreferredModel, "基建", "正面", "战斗", "背面");
			string fileBase = (string)model.Value["file"];
			fileBase = ValidateRelativeAssetPath(fileBase);

			Uri prefix = new Uri((string)meta["prefix"] ?? new Uri(metaUri, "./").AbsoluteUri);
			Uri atlasUri = new Uri(prefix, fileBase + ".atlas");
			Uri skeletonUri = new Uri(prefix, fileBase + ".skel");
			string relativeBase = Path.Combine("operators", characterId,
				fileBase.Replace('/', Path.DirectorySeparatorChar));
			List<string> resourceKeys = new List<string>();
			string atlasKey = AssetKey(characterId, skin.Name, model.Name, "atlas");
			string skeletonKey = AssetKey(characterId, skin.Name, model.Name, "skel");
			string atlasPath = await resources.GetOrDownloadAsync(new PrtsAssetRequest(
				atlasKey, atlasUri, relativeBase + ".atlas", resourceVersion
			), cancellationToken).ConfigureAwait(false);
			string skeletonPath = await resources.GetOrDownloadAsync(new PrtsAssetRequest(
				skeletonKey, skeletonUri, relativeBase + ".skel", resourceVersion
			), cancellationToken).ConfigureAwait(false);
			resourceKeys.Add(atlasKey);
			resourceKeys.Add(skeletonKey);

			List<string> texturePaths = new List<string>();
			IList<string> pages = ParseAtlasPages(File.ReadAllText(atlasPath));
			if (pages.Count == 0) throw new InvalidDataException("PRTS atlas has no texture pages");
			string localDirectory = Path.GetDirectoryName(relativeBase);
			for (int i = 0; i < pages.Count; i++) {
				string page = ValidateRelativeAssetPath(pages[i]);
				if (page.IndexOf('/') >= 0) throw new InvalidDataException("Nested atlas page paths are not supported: " + page);
				string pageRelativePath = Path.Combine(localDirectory, page);
				string pageKey = AssetKey(characterId, skin.Name, model.Name, "page:" + page);
				string pagePath = await resources.GetOrDownloadAsync(new PrtsAssetRequest(
					pageKey,
					new Uri(atlasUri, page), pageRelativePath, resourceVersion
				), cancellationToken).ConfigureAwait(false);
				texturePaths.Add(pagePath);
				resourceKeys.Add(pageKey);
			}

			return new OperatorAssetBundle(characterId, (string)meta["name"] ?? characterId,
				skin.Name, model.Name, resourceVersion, atlasPath, skeletonPath, texturePaths, resourceKeys);
		}

		private async Task<OperatorAssetFallbackManifest> LoadFallbackManifestAsync(
			CancellationToken cancellationToken
		) {
			PrtsAssetRequest request = new PrtsAssetRequest(
				"operator-fallback-manifest:" + FallbackReleaseTag,
				new Uri(FallbackManifestUrl),
				Path.Combine("fallback", "manifests", FallbackManifestName),
				FallbackReleaseTag
			);
			string path = await resources.GetOrDownloadAsync(request, cancellationToken).ConfigureAwait(false);
			return OperatorAssetFallbackManifest.Load(path);
		}

		private async Task<OperatorAssetBundle> ResolveFromFallbackFilesAsync(
			OperatorFallbackPackage package,
			OperatorFallbackAppearance appearance,
			CancellationToken cancellationToken
		) {
			OperatorFallbackFile atlasFile = appearance.FindFile("atlas");
			OperatorFallbackFile skeletonFile = appearance.FindFile("skel");
			List<string> resourceKeys = new List<string>();
			string atlasKey = AssetKeyForFile(package.CharacterId, appearance, atlasFile);
			string skeletonKey = AssetKeyForFile(package.CharacterId, appearance, skeletonFile);
			string atlasPath = await resources.GetOrDownloadAsync(
				CreateFallbackFileRequest(atlasKey, appearance, atlasFile),
				cancellationToken
			).ConfigureAwait(false);
			string skeletonPath = await resources.GetOrDownloadAsync(
				CreateFallbackFileRequest(skeletonKey, appearance, skeletonFile),
				cancellationToken
			).ConfigureAwait(false);
			resourceKeys.Add(atlasKey);
			resourceKeys.Add(skeletonKey);

			IList<string> pages = ParseAtlasPages(File.ReadAllText(atlasPath));
			if (pages.Count == 0)
				throw new InvalidDataException("Fallback atlas has no texture pages");
			List<string> texturePaths = new List<string>();
			for (int i = 0; i < pages.Count; i++) {
				string pageName = ValidateRelativeAssetPath(pages[i]);
				if (pageName.IndexOf('/') >= 0)
					throw new InvalidDataException("Nested atlas page paths are not supported: " + pageName);
				OperatorFallbackFile pageFile = appearance.FindFile("page", pageName);
				if (pageFile == null)
					throw new InvalidDataException("Fallback manifest has no atlas page: " + pageName);
				string pageKey = AssetKeyForFile(package.CharacterId, appearance, pageFile);
				texturePaths.Add(await resources.GetOrDownloadAsync(
					CreateFallbackFileRequest(pageKey, appearance, pageFile),
					cancellationToken
				).ConfigureAwait(false));
				resourceKeys.Add(pageKey);
			}

			return new OperatorAssetBundle(
				package.CharacterId,
				package.CharacterName,
				appearance.Skin,
				appearance.Model,
				appearance.ResourceVersion,
				atlasPath,
				skeletonPath,
				texturePaths,
				resourceKeys
			);
		}

		private static PrtsAssetRequest CreateFallbackFileRequest(
			string key,
			OperatorFallbackAppearance appearance,
			OperatorFallbackFile file
		) {
			string relativePath = ValidateRelativeAssetPath(file.RelativePath)
				.Replace('/', Path.DirectorySeparatorChar);
			return new PrtsAssetRequest(
				key,
				new Uri(file.SourceUrl),
				relativePath,
				appearance.ResourceVersion,
				file.Length,
				file.Sha256
			);
		}

		internal static string AssetKeyForFile(
			string characterId,
			OperatorFallbackAppearance appearance,
			OperatorFallbackFile file
		) {
			if (appearance == null) throw new ArgumentNullException("appearance");
			if (file == null) throw new ArgumentNullException("file");
			string type = string.Equals(file.Role, "page", StringComparison.OrdinalIgnoreCase)
				? "page:" + file.PageName
				: file.Role;
			return AssetKey(characterId, appearance.Skin, appearance.Model, type);
		}

		internal static IList<string> ParseAtlasPages(string atlasText) {
			List<string> pages = new List<string>();
			using (StringReader reader = new StringReader(atlasText ?? string.Empty)) {
				bool firstLineOfBlock = true;
				string line;
				while ((line = reader.ReadLine()) != null) {
					line = line.Trim();
					if (line.Length == 0) {
						firstLineOfBlock = true;
						continue;
					}
					if (!firstLineOfBlock) continue;
					pages.Add(line);
					firstLineOfBlock = false;
				}
			}
			return pages;
		}

		private static JProperty ChooseProperty(JObject source, params string[] preferences) {
			for (int p = 0; p < preferences.Length; p++) {
				string preferred = preferences[p];
				if (string.IsNullOrWhiteSpace(preferred)) continue;
				foreach (JProperty property in source.Properties()) {
					if (string.Equals(property.Name, preferred, StringComparison.OrdinalIgnoreCase)) return property;
				}
			}
			foreach (JProperty property in source.Properties()) return property;
			throw new InvalidDataException("PRTS metadata object is empty");
		}

		private static string ValidatePathToken(string value, string label) {
			if (string.IsNullOrWhiteSpace(value) || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.Contains(".."))
				throw new InvalidDataException("Invalid " + label);
			return value.Trim();
		}

		private static string ValidateRelativeAssetPath(string value) {
			if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("PRTS asset path is empty");
			string normalized = value.Trim().Replace('\\', '/');
			if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../") || normalized == ".." ||
				normalized.Contains(":") || normalized.Contains("//"))
				throw new InvalidDataException("Invalid PRTS asset path: " + value);
			return normalized;
		}

		private static string AssetKey(string characterId, string skin, string model, string type) {
			return "operator:" + characterId + ":" + skin + ":" + model + ":" + type;
		}

		private static string ComputeSha256(string value) {
			using (SHA256 sha = SHA256.Create()) {
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
				return BitConverter.ToString(hash).Replace("-", string.Empty);
			}
		}
	}
}
