using System;
using KSerialization;

namespace ArknightsOperatorsMod {
	[SerializationConfig(MemberSerialization.OptIn)]
	public sealed class OperatorAppearanceOverride : KMonoBehaviour {
		[Serialize]
		private bool hasOverride;

		[Serialize]
		private string characterId;

		[Serialize]
		private string skin;

		[Serialize]
		private string model;

		internal bool HasOverride {
			get { return hasOverride; }
		}

		internal ModConfig Resolve(ModConfig globalConfig) {
			if (globalConfig == null) throw new ArgumentNullException("globalConfig");
			ModConfig resolved = ModConfigStore.Clone(globalConfig);
			if (!hasOverride) return resolved;
			resolved.DefaultCharacterId = characterId;
			resolved.PreferredSkin = skin;
			resolved.PreferredModel = model;
			resolved.Normalize();
			return resolved;
		}

		internal void Set(string nextCharacterId, string nextSkin, string nextModel) {
			if (string.IsNullOrWhiteSpace(nextCharacterId))
				throw new ArgumentException("Character ID is required", "nextCharacterId");
			if (string.IsNullOrWhiteSpace(nextSkin))
				throw new ArgumentException("Skin is required", "nextSkin");
			if (string.IsNullOrWhiteSpace(nextModel))
				throw new ArgumentException("Model is required", "nextModel");
			characterId = nextCharacterId;
			skin = nextSkin;
			model = nextModel;
			hasOverride = true;
		}

		internal void Clear() {
			hasOverride = false;
			characterId = null;
			skin = null;
			model = null;
		}
	}
}
