using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AmiyaDuplicantMod {
	public sealed class OperatorAssetFallbackManifest {
		public const int CurrentSchemaVersion = 1;

		[JsonProperty("schema_version")]
		public int SchemaVersion { get; set; }

		[JsonProperty("snapshot_id")]
		public string SnapshotId { get; set; }

		[JsonProperty("release_tag")]
		public string ReleaseTag { get; set; }

		[JsonProperty("operators")]
		public List<OperatorFallbackPackage> Operators { get; set; }

		public static OperatorAssetFallbackManifest Load(string path) {
			if (string.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");
			return Parse(File.ReadAllText(path));
		}

		internal static OperatorAssetFallbackManifest Parse(string json) {
			OperatorAssetFallbackManifest manifest =
				JsonConvert.DeserializeObject<OperatorAssetFallbackManifest>(json);
			if (manifest == null || manifest.SchemaVersion != CurrentSchemaVersion)
				throw new InvalidDataException("Unsupported operator fallback manifest schema");
			if (string.IsNullOrWhiteSpace(manifest.SnapshotId) ||
				string.IsNullOrWhiteSpace(manifest.ReleaseTag) || manifest.Operators == null)
				throw new InvalidDataException("Operator fallback manifest header is incomplete");
			for (int i = 0; i < manifest.Operators.Count; i++)
				manifest.Operators[i].Validate();
			return manifest;
		}

		public OperatorFallbackAppearance Choose(
			string characterId,
			string preferredSkin,
			string preferredModel,
			out OperatorFallbackPackage package
		) {
			package = Operators.FirstOrDefault(candidate => string.Equals(
				candidate.CharacterId,
				characterId,
				StringComparison.OrdinalIgnoreCase
			));
			if (package == null)
				return null;

			IEnumerable<OperatorFallbackAppearance> appearances = package.Appearances;
			List<OperatorFallbackAppearance> selectedSkin = appearances.Where(candidate =>
				string.Equals(candidate.Skin, preferredSkin, StringComparison.OrdinalIgnoreCase)
			).ToList();
			if (selectedSkin.Count == 0)
				selectedSkin = appearances.Where(candidate =>
					string.Equals(candidate.Skin, "默认", StringComparison.OrdinalIgnoreCase)
				).ToList();
			if (selectedSkin.Count == 0) {
				OperatorFallbackAppearance first = appearances.FirstOrDefault();
				if (first != null)
					selectedSkin = appearances.Where(candidate => string.Equals(
						candidate.Skin,
						first.Skin,
						StringComparison.OrdinalIgnoreCase
					)).ToList();
			}
			if (selectedSkin.Count == 0)
				return null;

			OperatorFallbackAppearance exactModel = selectedSkin.FirstOrDefault(candidate =>
				string.Equals(candidate.Model, preferredModel, StringComparison.OrdinalIgnoreCase)
			);
			if (exactModel != null)
				return exactModel;
			string[] modelOrder = { "基建", "正面", "战斗", "背面" };
			for (int i = 0; i < modelOrder.Length; i++) {
				OperatorFallbackAppearance preferred = selectedSkin.FirstOrDefault(candidate =>
					string.Equals(candidate.Model, modelOrder[i], StringComparison.OrdinalIgnoreCase)
				);
				if (preferred != null)
					return preferred;
			}
			return selectedSkin[0];
		}
	}

	public sealed class OperatorFallbackPackage {
		[JsonProperty("character_id")]
		public string CharacterId { get; set; }

		[JsonProperty("character_name")]
		public string CharacterName { get; set; }

		[JsonProperty("package_url")]
		public string PackageUrl { get; set; }

		[JsonProperty("package_length")]
		public long PackageLength { get; set; }

		[JsonProperty("package_sha256")]
		public string PackageSha256 { get; set; }

		[JsonProperty("appearances")]
		public List<OperatorFallbackAppearance> Appearances { get; set; }

		internal void Validate() {
			Uri packageUri;
			if (string.IsNullOrWhiteSpace(CharacterId) || string.IsNullOrWhiteSpace(CharacterName) ||
				!Uri.TryCreate(PackageUrl, UriKind.Absolute, out packageUri) ||
				PackageLength <= 0L || !IsSha256(PackageSha256) ||
				Appearances == null || Appearances.Count == 0)
				throw new InvalidDataException("Operator fallback package is incomplete: " + CharacterId);
			for (int i = 0; i < Appearances.Count; i++)
				Appearances[i].Validate(CharacterId);
		}

		internal static bool IsSha256(string value) {
			if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
				return false;
			for (int i = 0; i < value.Length; i++) {
				char c = value[i];
				if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
					return false;
			}
			return true;
		}
	}

	public sealed class OperatorFallbackAppearance {
		[JsonProperty("skin")]
		public string Skin { get; set; }

		[JsonProperty("model")]
		public string Model { get; set; }

		[JsonProperty("resource_version")]
		public string ResourceVersion { get; set; }

		[JsonProperty("files")]
		public List<OperatorFallbackFile> Files { get; set; }

		public OperatorFallbackFile FindFile(string role, string pageName = null) {
			return Files.FirstOrDefault(file =>
				string.Equals(file.Role, role, StringComparison.OrdinalIgnoreCase) &&
				(pageName == null || string.Equals(file.PageName, pageName, StringComparison.Ordinal))
			);
		}

		internal void Validate(string characterId) {
			if (string.IsNullOrWhiteSpace(Skin) || string.IsNullOrWhiteSpace(Model) ||
				string.IsNullOrWhiteSpace(ResourceVersion) || Files == null || Files.Count < 3)
				throw new InvalidDataException("Fallback appearance is incomplete: " + characterId);
			for (int i = 0; i < Files.Count; i++)
				Files[i].Validate(characterId);
			if (FindFile("atlas") == null || FindFile("skel") == null ||
				!Files.Any(file => string.Equals(file.Role, "page", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException("Fallback appearance is missing Spine files: " + characterId);
		}
	}

	public sealed class OperatorFallbackFile {
		[JsonProperty("role")]
		public string Role { get; set; }

		[JsonProperty("page_name")]
		public string PageName { get; set; }

		[JsonProperty("relative_path")]
		public string RelativePath { get; set; }

		[JsonProperty("archive_path")]
		public string ArchivePath { get; set; }

		[JsonProperty("source_url")]
		public string SourceUrl { get; set; }

		[JsonProperty("length")]
		public long Length { get; set; }

		[JsonProperty("sha256")]
		public string Sha256 { get; set; }

		internal void Validate(string characterId) {
			Uri sourceUri;
			bool validRole = string.Equals(Role, "atlas", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(Role, "skel", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(Role, "page", StringComparison.OrdinalIgnoreCase);
			if (!validRole || string.IsNullOrWhiteSpace(RelativePath) ||
				string.IsNullOrWhiteSpace(ArchivePath) ||
				!Uri.TryCreate(SourceUrl, UriKind.Absolute, out sourceUri) || Length <= 0L ||
				!OperatorFallbackPackage.IsSha256(Sha256))
				throw new InvalidDataException("Fallback file is incomplete: " + characterId);
			if (string.Equals(Role, "page", StringComparison.OrdinalIgnoreCase) &&
				string.IsNullOrWhiteSpace(PageName))
				throw new InvalidDataException("Fallback atlas page name is missing: " + characterId);
		}
	}
}
