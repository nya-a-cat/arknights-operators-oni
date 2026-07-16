using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PeterHan.PLib.Options;
using PeterHan.PLib.UI;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class OperatorAppearanceOptionsEntry : OptionsEntry {
		private const int MaximumOperatorMatches = 60;
		private static bool hasPendingSelection;
		private static ResourcePersistencePolicy pendingDownloadPolicy;
		private static int pendingCacheCapacityMiB;
		private static bool pendingCacheCapacityInputValid;
		private static string pendingCharacterId;
		private static string pendingSkin;
		private static string pendingModel;
		private static bool pendingAutomaticModelSwitching;
		private static readonly Type ComboComponentType = typeof(PComboBox<Choice>).Assembly.GetType(
			"PeterHan.PLib.UI.PComboBoxComponent"
		);
		private static readonly MethodInfo SetComboItems = ComboComponentType == null ? null :
			ComboComponentType.GetMethod("SetItems", BindingFlags.Public | BindingFlags.Instance);
		private static readonly PropertyInfo ComboContentContainer = ComboComponentType == null ? null :
			ComboComponentType.GetProperty("ContentContainer", BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly OperatorAppearanceCatalog catalog;
		private OperatorAppearanceSelection selection;
		private GameObject policyComboObject;
		private GameObject searchFieldObject;
		private GameObject operatorComboObject;
		private GameObject skinComboObject;
		private GameObject modelComboObject;
		private GameObject automaticSwitchingComboObject;
		private GameObject cacheCapacityFieldObject;
		private GameObject cacheUsageLabelObject;
		private GameObject dialogAcceptButtonObject;
		private ResourcePersistencePolicy downloadPolicy;
		private int cacheCapacityMiB;
		private bool cacheCapacityInputValid = true;
		private bool automaticModelSwitching;
		private string value;

		public override string Name {
			get { return "OperatorAppearance"; }
		}

		public override object Value {
			get { return value; }
			set { this.value = value as string; }
		}

		public OperatorAppearanceOptionsEntry(ModConfig config) : base(null, new OptionAttribute(
			ModLocalization.Text("明日方舟干员外观", "Arknights operator appearance"),
			ModLocalization.Text(
				"选择全局干员、皮肤、模型和资源策略。保存后会更新当前存档内已有复制人。",
				"Select the global operator, skin, model, and resource policy. Saving updates existing duplicants in the current colony."
			)
		)) {
			try {
				catalog = OperatorAppearanceCatalog.Load(ModAssets.OperatorCatalogPath);
			} catch (Exception error) {
				Debug.LogWarning("[ArknightsOperatorsMod] Operator catalog load failed: " + error.Message);
				catalog = OperatorAppearanceCatalog.FromJson(
					"{\"schema_version\":1,\"operators\":[{\"id\":\"char_002_amiya\"," +
					"\"name\":\"阿米娅\",\"skins\":[{\"name\":\"默认\",\"models\":[\"基建\"]}]}]}"
				);
			}
			config.Normalize();
			selection = catalog.Normalize(config.DefaultCharacterId, config.PreferredSkin,
				config.PreferredModel);
			downloadPolicy = config.DownloadPolicy;
			cacheCapacityMiB = config.CacheCapacityMiB;
			automaticModelSwitching = config.AutomaticModelSwitching;
			value = BuildValue(selection);
			StagePendingSelection();
		}

		internal static bool ApplyPendingSelection(ModConfig config) {
			if (config == null) throw new ArgumentNullException("config");
			if (!hasPendingSelection) return false;
			if (!pendingCacheCapacityInputValid) {
				Debug.LogWarning("[ArknightsOperatorsMod] Settings were not applied because the cache " +
					"capacity is outside 128-2000 MiB");
				return false;
			}
			bool changed = config.DownloadPolicy != pendingDownloadPolicy ||
				config.CacheCapacityMiB != pendingCacheCapacityMiB ||
				config.AutomaticModelSwitching != pendingAutomaticModelSwitching ||
				!string.Equals(config.DefaultCharacterId, pendingCharacterId,
					StringComparison.Ordinal) ||
				!string.Equals(config.PreferredSkin, pendingSkin, StringComparison.Ordinal) ||
				!string.Equals(config.PreferredModel, pendingModel, StringComparison.Ordinal);
			config.DownloadPolicy = pendingDownloadPolicy;
			config.CacheCapacityMiB = pendingCacheCapacityMiB;
			config.DefaultCharacterId = pendingCharacterId;
			config.PreferredSkin = pendingSkin;
			config.PreferredModel = pendingModel;
			config.AutomaticModelSwitching = pendingAutomaticModelSwitching;
			return changed;
		}

		public override void CreateUIEntry(PGridPanel parent, ref int row) {
			List<Choice> policyChoices = BuildPolicyChoices();
			PComboBox<Choice> policies = new PComboBox<Choice>("ResourcePolicyChoice") {
				Content = policyChoices,
				InitialItem = FindChoice(policyChoices, downloadPolicy.ToString()),
				MaxRowsShown = 2,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = ModLocalization.Text(
					"按需缓存会限制磁盘占用；永久保留适合长期离线使用已经访问过的外观。",
					"On-demand caching bounds disk usage; permanent retention keeps visited appearances available offline."
				),
				OnOptionSelected = OnPolicySelected
			};
			policies.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				policyComboObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("资源策略", "Resource policy"), policies,
				policies.ToolTip);

			parent.AddRow(new GridRowSpec());
			PTextField capacity = new PTextField("CacheCapacityMiB") {
				Text = cacheCapacityMiB.ToString(CultureInfo.InvariantCulture),
				PlaceholderText = ModConfig.DefaultCacheCapacityMiB.ToString(CultureInfo.InvariantCulture),
				ToolTip = ModLocalization.Text(
					"按需缓存容量，单位为 MiB。请输入 128 到 2000 之间的整数；永久保留模式会保留此数值供以后使用。",
					"On-demand cache capacity in MiB. Enter an integer from 128 to 2000. Permanent retention keeps this value for later use."
				),
				MaxLength = 4,
				OnTextChanged = OnCacheCapacityChanged
			};
			capacity.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				cacheCapacityFieldObject = realized;
				SetTextFieldIntegerOnly(realized);
				SetTextFieldEnabled(realized,
					downloadPolicy == ResourcePersistencePolicy.OnDemandCache);
				OperatorAppearanceOptionsDialogBridge bridge =
					realized.AddComponent<OperatorAppearanceOptionsDialogBridge>();
				bridge.Initialize(this);
			});
			AddRow(parent, row++, ModLocalization.Text("缓存容量（MiB）", "Cache capacity (MiB)"),
				capacity, capacity.ToolTip);

			parent.AddRow(new GridRowSpec());
			PLabel usage = new PLabel("CacheUsage") {
				Text = BuildCacheUsageText(),
				ToolTip = ModLocalization.Text(
					"显示索引中的当前缓存占用和目标容量。正在使用的资源会受到保护。",
					"Shows indexed cache usage and the target capacity. Resources currently in use are protected."
				),
				TextStyle = PUITuning.Fonts.TextLightStyle
			};
			usage.AddOnRealize(realized => { cacheUsageLabelObject = realized; });
			AddRow(parent, row++, ModLocalization.Text("当前占用 / 目标", "Current usage / target"),
				usage, usage.ToolTip);

			parent.AddRow(new GridRowSpec());
			PTextField search = new PTextField("OperatorSearch") {
				Text = GetOperatorName(selection.Character),
				PlaceholderText = ModLocalization.Text(
					"输入中文、英文、日文、别名或 char_id",
					"Enter a Chinese, English, or Japanese name, alias, or char_id"),
				ToolTip = ModLocalization.Text(
					"支持中文名、英文名、日文名、PRTS 重定向别名和 char_id；多个匹配项会显示在下方列表。",
					"Supports Chinese, English, and Japanese names, PRTS redirect aliases, and char_id. Multiple matches appear below."
				),
				MaxLength = 80,
				OnTextChanged = OnSearchChanged
			};
			search.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				searchFieldObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("搜索干员", "Search operator"), search,
				search.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> operatorChoices = BuildOperatorChoices(selection.Character.Name);
			Choice selectedOperator = FindChoice(operatorChoices, selection.Character.Id);
			PComboBox<Choice> operators = new PComboBox<Choice>("OperatorChoice") {
				Content = operatorChoices,
				InitialItem = selectedOperator,
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = ModLocalization.Text(
					"按中文名、英文名、日文名或别名搜索，再从匹配结果中选择。",
					"Search by a Chinese, English, or Japanese name or alias, then choose from the matching operators."
				),
				OnOptionSelected = OnOperatorSelected
			};
			operators.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				operatorComboObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("干员", "Operator"), operators,
				operators.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> skinChoices = BuildSkinChoices();
			PComboBox<Choice> skins = new PComboBox<Choice>("SkinChoice") {
				Content = skinChoices,
				InitialItem = FindChoice(skinChoices, selection.Skin.Name),
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = ModLocalization.Text(
					"皮肤列表随干员联动。", "The skin list follows the selected operator."),
				OnOptionSelected = OnSkinSelected
			};
			skins.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				skinComboObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("皮肤", "Skin"), skins, skins.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> modelChoices = BuildModelChoices();
			PComboBox<Choice> models = new PComboBox<Choice>("ModelChoice") {
				Content = modelChoices,
				InitialItem = FindChoice(modelChoices, selection.Model),
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = ModLocalization.Text(
					"模型列表随所选皮肤联动；基建模型通常最适合复制人日常动作。",
					"The model list follows the selected skin. Base models usually fit daily duplicant actions best."
				),
				OnOptionSelected = OnModelSelected
			};
			models.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				modelComboObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("模型", "Model"), models,
				models.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> switchingChoices = BuildAutomaticModelSwitchingChoices();
			PComboBox<Choice> switching = new PComboBox<Choice>("AutomaticModelSwitchingChoice") {
				Content = switchingChoices,
				InitialItem = FindChoice(switchingChoices, automaticModelSwitching.ToString()),
				MaxRowsShown = 2,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = ModLocalization.Text(
					"日常、移动、睡觉使用基建模型；挖矿、战斗、眩晕和死亡使用正面战斗模型。",
					"Use the base model for daily, movement, and sleep states; use the front combat model for digging, combat, stun, and death."
				),
				OnOptionSelected = OnAutomaticModelSwitchingSelected
			};
			switching.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				automaticSwitchingComboObject = realized;
			});
			AddRow(parent, row++, ModLocalization.Text("自动模型切换", "Automatic model switching"),
				switching, switching.ToolTip);
		}

		public override GameObject GetUIComponent() {
			return new PLabel("OperatorAppearancePlaceholder") { Text = "" }.Build();
		}

		public override void ReadFrom(object settings) {
			ModConfig config = settings as ModConfig;
			if (config == null) return;
			config.Normalize();
			selection = catalog.Normalize(config.DefaultCharacterId, config.PreferredSkin,
				config.PreferredModel);
			downloadPolicy = config.DownloadPolicy;
			cacheCapacityMiB = config.CacheCapacityMiB;
			cacheCapacityInputValid = true;
			automaticModelSwitching = config.AutomaticModelSwitching;
			value = BuildValue(selection);
			RefreshAllControls();
			StagePendingSelection();
		}

		public override bool WriteTo(object settings) {
			ModConfig config = settings as ModConfig;
			if (config == null || selection == null) return false;
			if (!ModConfig.CanApplyCacheCapacityInput(downloadPolicy, cacheCapacityInputValid))
				return false;
			bool changed = config.DownloadPolicy != downloadPolicy ||
				config.CacheCapacityMiB != cacheCapacityMiB ||
				config.AutomaticModelSwitching != automaticModelSwitching ||
				!string.Equals(config.DefaultCharacterId, selection.Character.Id,
				StringComparison.Ordinal) || !string.Equals(config.PreferredSkin, selection.Skin.Name,
				StringComparison.Ordinal) || !string.Equals(config.PreferredModel, selection.Model,
				StringComparison.Ordinal);
			config.DownloadPolicy = downloadPolicy;
			config.CacheCapacityMiB = cacheCapacityMiB;
			config.DefaultCharacterId = selection.Character.Id;
			config.PreferredSkin = selection.Skin.Name;
			config.PreferredModel = selection.Model;
			config.AutomaticModelSwitching = automaticModelSwitching;
			return changed;
		}

		private void OnPolicySelected(GameObject source, Choice choice) {
			if (choice == null) return;
			downloadPolicy = string.Equals(choice.Value, ResourcePersistencePolicy.Permanent.ToString(),
				StringComparison.Ordinal) ? ResourcePersistencePolicy.Permanent :
					ResourcePersistencePolicy.OnDemandCache;
			if (downloadPolicy == ResourcePersistencePolicy.Permanent && !cacheCapacityInputValid) {
				cacheCapacityInputValid = true;
				SetTextFieldTextWithoutNotify(cacheCapacityFieldObject,
					cacheCapacityMiB.ToString(CultureInfo.InvariantCulture));
			}
			SetTextFieldEnabled(cacheCapacityFieldObject,
				downloadPolicy == ResourcePersistencePolicy.OnDemandCache);
			RefreshCacheUsageLabel();
			StagePendingSelection();
			RefreshSaveButton();
		}

		private void OnCacheCapacityChanged(GameObject source, string text) {
			int parsed;
			if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) ||
				!ModConfig.IsValidCacheCapacityMiB(parsed)) {
				cacheCapacityInputValid = false;
				RefreshCacheUsageLabel();
				StagePendingSelection();
				RefreshSaveButton();
				return;
			}
			cacheCapacityInputValid = true;
			cacheCapacityMiB = parsed;
			RefreshCacheUsageLabel();
			StagePendingSelection();
			RefreshSaveButton();
		}

		private void OnAutomaticModelSwitchingSelected(GameObject source, Choice choice) {
			if (choice == null) return;
			bool parsed;
			automaticModelSwitching = bool.TryParse(choice.Value, out parsed) && parsed;
			StagePendingSelection();
		}

		private void OnSearchChanged(GameObject source, string text) {
			OperatorAppearanceDefinition exact = catalog.FindExact(text);
			if (exact != null) SelectOperator(exact.Id);
			RefreshOperatorCombo(BuildOperatorChoices(text));
		}

		private void OnOperatorSelected(GameObject source, Choice choice) {
			if (choice != null) SelectOperator(choice.Value);
		}

		private void OnSkinSelected(GameObject source, Choice choice) {
			if (choice == null) return;
			selection = catalog.Normalize(selection.Character.Id, choice.Value, selection.Model);
			value = BuildValue(selection);
			StagePendingSelection();
			RefreshSkinAndModelCombos();
		}

		private void OnModelSelected(GameObject source, Choice choice) {
			if (choice == null) return;
			selection = catalog.Normalize(selection.Character.Id, selection.Skin.Name, choice.Value);
			value = BuildValue(selection);
			StagePendingSelection();
		}

		private void SelectOperator(string characterId) {
			selection = catalog.Normalize(characterId, selection.Skin.Name, selection.Model);
			value = BuildValue(selection);
			StagePendingSelection();
			RefreshSkinAndModelCombos();
		}

		private void StagePendingSelection() {
			if (selection == null) return;
			pendingDownloadPolicy = downloadPolicy;
			pendingCacheCapacityMiB = cacheCapacityMiB;
			pendingCacheCapacityInputValid = ModConfig.CanApplyCacheCapacityInput(
				downloadPolicy,
				cacheCapacityInputValid
			);
			pendingCharacterId = selection.Character.Id;
			pendingSkin = selection.Skin.Name;
			pendingModel = selection.Model;
			pendingAutomaticModelSwitching = automaticModelSwitching;
			hasPendingSelection = true;
		}

		private List<Choice> BuildOperatorChoices(string query) {
			IList<OperatorAppearanceDefinition> matches = catalog.Search(query, MaximumOperatorMatches);
			List<Choice> choices = new List<Choice>(matches.Count + 1);
			for (int i = 0; i < matches.Count; i++) {
				OperatorAppearanceDefinition item = matches[i];
				choices.Add(new Choice(item.Id, GetOperatorLabel(item), item.Id));
			}
			if (FindChoice(choices, selection.Character.Id) == null) {
				choices.Insert(0, new Choice(selection.Character.Id,
					GetOperatorLabel(selection.Character),
					selection.Character.Id));
			}
			return choices;
		}

		private static string GetOperatorName(OperatorAppearanceDefinition item) {
			if (ModLocalization.UseJapanese && !string.IsNullOrWhiteSpace(item.JapaneseName))
				return item.JapaneseName;
			return !ModLocalization.UseChinese && !string.IsNullOrWhiteSpace(item.EnglishName) ?
				item.EnglishName : item.Name;
		}

		private static string GetOperatorLabel(OperatorAppearanceDefinition item) {
			string primary = GetOperatorName(item);
			string secondary = ModLocalization.UseChinese ? item.EnglishName : item.Name;
			if (!string.IsNullOrWhiteSpace(secondary) &&
				!string.Equals(primary, secondary, StringComparison.OrdinalIgnoreCase))
				return primary + " / " + secondary + "  [" + item.Id + "]";
			return primary + "  [" + item.Id + "]";
		}

		private static List<Choice> BuildPolicyChoices() {
			return new List<Choice> {
				new Choice(ResourcePersistencePolicy.OnDemandCache.ToString(),
					ModLocalization.Text("按需缓存（容量可设置）", "On-demand cache (configurable)"),
					ModLocalization.Text("限制磁盘占用，自动清理未引用的旧资源。",
						"Bounds disk usage and removes old unreferenced resources.")),
				new Choice(ResourcePersistencePolicy.Permanent.ToString(),
					ModLocalization.Text("永久保留已下载资源", "Keep downloaded resources"),
					ModLocalization.Text("保留已经下载的资源，便于长期离线使用。",
						"Keeps downloaded resources for long-term offline reuse."))
			};
		}

		private static List<Choice> BuildAutomaticModelSwitchingChoices() {
			return new List<Choice> {
				new Choice(bool.TrueString,
					ModLocalization.Text("开启", "Enabled"),
					ModLocalization.Text("按 ONI 状态选择基建或战斗模型。",
						"Select the base or combat model from the current ONI state.")),
				new Choice(bool.FalseString,
					ModLocalization.Text("关闭", "Disabled"),
					ModLocalization.Text("始终使用手动选择的模型。",
						"Always use the manually selected model."))
			};
		}

		private List<Choice> BuildSkinChoices() {
			List<Choice> choices = new List<Choice>(selection.Character.Skins.Count);
			for (int i = 0; i < selection.Character.Skins.Count; i++) {
				string name = selection.Character.Skins[i].Name;
				string label = !ModLocalization.UseChinese && name == "默认" ?
					"Default (默认)" : name;
				choices.Add(new Choice(name, label, name));
			}
			return choices;
		}

		private List<Choice> BuildModelChoices() {
			List<Choice> choices = new List<Choice>(selection.Skin.Models.Count);
			for (int i = 0; i < selection.Skin.Models.Count; i++) {
				string name = selection.Skin.Models[i];
				string label = name;
				if (!ModLocalization.UseChinese) {
					if (name == "基建") label = "Base (基建)";
					else if (name == "正面") label = "Front (正面)";
					else if (name == "背面") label = "Back (背面)";
				}
				choices.Add(new Choice(name, label, name));
			}
			return choices;
		}

		private void RefreshOperatorCombo(List<Choice> choices) {
			RefreshCombo(operatorComboObject, choices, selection.Character.Id);
		}

		private void RefreshSkinAndModelCombos() {
			RefreshCombo(skinComboObject, BuildSkinChoices(), selection.Skin.Name);
			RefreshCombo(modelComboObject, BuildModelChoices(), selection.Model);
		}

		private void RefreshAllControls() {
			List<Choice> policies = BuildPolicyChoices();
			if (policyComboObject != null)
				PComboBox<Choice>.SetSelectedItem(policyComboObject,
					FindChoice(policies, downloadPolicy.ToString()), false);
			SetTextFieldTextWithoutNotify(cacheCapacityFieldObject,
				cacheCapacityMiB.ToString(CultureInfo.InvariantCulture));
			SetTextFieldEnabled(cacheCapacityFieldObject,
				downloadPolicy == ResourcePersistencePolicy.OnDemandCache);
			SetTextFieldTextWithoutNotify(searchFieldObject, GetOperatorName(selection.Character));
			RefreshOperatorCombo(BuildOperatorChoices(selection.Character.Name));
			RefreshSkinAndModelCombos();
			List<Choice> switching = BuildAutomaticModelSwitchingChoices();
			if (automaticSwitchingComboObject != null)
				PComboBox<Choice>.SetSelectedItem(automaticSwitchingComboObject,
					FindChoice(switching, automaticModelSwitching.ToString()), false);
			RefreshCacheUsageLabel();
			RefreshSaveButton();
		}

		internal void SetDialogAcceptButton(GameObject acceptButton) {
			dialogAcceptButtonObject = acceptButton;
			RefreshSaveButton();
		}

		internal void HandleDialogReset() {
			ReadFrom(new ModConfig());
		}

		private void RefreshSaveButton() {
			if (dialogAcceptButtonObject == null) return;
			PButton.SetButtonEnabled(dialogAcceptButtonObject,
				ModConfig.CanApplyCacheCapacityInput(downloadPolicy, cacheCapacityInputValid));
		}

		private static void RefreshCombo(GameObject combo, List<Choice> choices, string selectedValue) {
			if (combo == null || ComboComponentType == null || SetComboItems == null) return;
			Component component = combo.GetComponent(ComboComponentType);
			if (component == null) return;
			RectTransform content = ComboContentContainer == null ? null :
				ComboContentContainer.GetValue(component, null) as RectTransform;
			if (content != null) {
				while (content.childCount > 0) {
					Transform row = content.GetChild(content.childCount - 1);
					row.gameObject.SetActive(false);
					row.SetParent(null, false);
					UnityEngine.Object.Destroy(row.gameObject);
				}
			}
			SetComboItems.Invoke(component, new object[] { choices });
			PComboBox<Choice>.SetSelectedItem(combo, FindChoice(choices, selectedValue), false);
		}

		private static Choice FindChoice(IList<Choice> choices, string value) {
			for (int i = 0; i < choices.Count; i++) {
				if (string.Equals(choices[i].Value, value, StringComparison.OrdinalIgnoreCase))
					return choices[i];
			}
			return null;
		}

		private static string BuildValue(OperatorAppearanceSelection current) {
			return current.Character.Id + "|" + current.Skin.Name + "|" + current.Model;
		}

		private string BuildCacheUsageText() {
			if (downloadPolicy == ResourcePersistencePolicy.OnDemandCache &&
				!cacheCapacityInputValid)
				return ModLocalization.Text("请输入 128 到 2000 之间的整数",
					"Enter an integer from 128 to 2000");
			long bytes = 0L;
			try {
				if (PrtsResourceService.Instance != null)
					bytes = PrtsResourceService.Instance.IndexedDiskUsage;
			} catch (Exception error) {
				Debug.LogWarning("[ArknightsOperatorsMod] Failed to read cache usage: " + error.Message);
			}
			string usage = (bytes / (1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture) +
				" MiB";
			string target = downloadPolicy == ResourcePersistencePolicy.Permanent ?
				ModLocalization.Text("永久保留", "Permanent") :
				cacheCapacityMiB.ToString(CultureInfo.InvariantCulture) + " MiB";
			string result = usage + " / " + target;
			if (ModConfig.IsCacheUsageOverTarget(bytes, downloadPolicy, cacheCapacityMiB))
				result += "\n" + ModLocalization.Text(
					"受保护资源使占用暂时超过目标，释放后会继续清理。",
					"Protected resources temporarily exceed the target; cleanup resumes after release."
				);
			return result;
		}

		private void RefreshCacheUsageLabel() {
			SetComponentStringProperty(cacheUsageLabelObject, "text", BuildCacheUsageText());
		}

		private static void SetTextFieldEnabled(GameObject field, bool enabled) {
			SetComponentBooleanProperty(field, "interactable", enabled);
		}

		private static void SetTextFieldTextWithoutNotify(GameObject field, string text) {
			if (field == null) return;
			Component[] components = field.GetComponentsInChildren<Component>(true);
			for (int i = 0; i < components.Length; i++) {
				MethodInfo method = components[i].GetType().GetMethod("SetTextWithoutNotify",
					BindingFlags.Public | BindingFlags.Instance, null,
					new[] { typeof(string) }, null);
				if (method == null) continue;
				method.Invoke(components[i], new object[] { text });
				return;
			}
			SetComponentStringProperty(field, "text", text);
		}

		private static void SetTextFieldIntegerOnly(GameObject field) {
			if (field == null) return;
			Component[] components = field.GetComponentsInChildren<Component>(true);
			for (int i = 0; i < components.Length; i++) {
				PropertyInfo property = components[i].GetType().GetProperty("contentType",
					BindingFlags.Public | BindingFlags.Instance);
				if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
					continue;
				try {
					object integer = Enum.Parse(property.PropertyType, "IntegerNumber", false);
					property.SetValue(components[i], integer, null);
				} catch (ArgumentException) {
				}
			}
		}

		private static void SetComponentBooleanProperty(GameObject root, string propertyName,
			bool value) {
			if (root == null) return;
			Component[] components = root.GetComponentsInChildren<Component>(true);
			for (int i = 0; i < components.Length; i++) {
				PropertyInfo property = components[i].GetType().GetProperty(propertyName,
					BindingFlags.Public | BindingFlags.Instance);
				if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
					property.SetValue(components[i], value, null);
			}
		}

		private static void SetComponentStringProperty(GameObject root, string propertyName,
			string value) {
			if (root == null) return;
			Component[] components = root.GetComponentsInChildren<Component>(true);
			for (int i = 0; i < components.Length; i++) {
				PropertyInfo property = components[i].GetType().GetProperty(propertyName,
					BindingFlags.Public | BindingFlags.Instance);
				if (property != null && property.CanWrite && property.PropertyType == typeof(string)) {
					property.SetValue(components[i], value, null);
					return;
				}
			}
		}

		private static void AddRow(PGridPanel parent, int row, string label, IUIComponent component,
			string tooltip) {
			parent.AddChild(new PLabel("Label") {
				Text = label,
				ToolTip = tooltip,
				TextStyle = PUITuning.Fonts.TextLightStyle
			}, new GridComponentSpec(row, 0) {
				Margin = LABEL_MARGIN,
				Alignment = TextAnchor.MiddleLeft
			});
			parent.AddChild(component, new GridComponentSpec(row, 1) {
				Alignment = TextAnchor.MiddleRight,
				Margin = CONTROL_MARGIN
			});
		}

		private sealed class Choice : IListableOption, ITooltipListableOption {
			public readonly string Value;
			private readonly string label;
			private readonly string tooltip;

			public Choice(string value, string label, string tooltip) {
				Value = value;
				this.label = label;
				this.tooltip = tooltip;
			}

			public string GetProperName() {
				return label;
			}

			public string GetToolTipText() {
				return tooltip;
			}
		}
	}

	internal sealed class OperatorAppearanceOptionsDialogBridge : MonoBehaviour {
		private OperatorAppearanceOptionsEntry owner;
		private KButton resetButton;

		internal void Initialize(OperatorAppearanceOptionsEntry entry) {
			owner = entry;
		}

		private void Start() {
			if (owner == null) return;
			Transform dialog = FindDialogRoot(transform);
			GameObject accept = FindNamedChild(dialog, "ok");
			owner.SetDialogAcceptButton(accept);
			GameObject reset = FindNamedChild(dialog, "ResetConfig");
			resetButton = reset == null ? null : reset.GetComponent<KButton>();
			if (resetButton != null) resetButton.onClick += OnReset;
		}

		private void OnDestroy() {
			if (resetButton != null) resetButton.onClick -= OnReset;
			resetButton = null;
			owner = null;
		}

		private void OnReset() {
			if (owner != null) owner.HandleDialogReset();
		}

		private static Transform FindDialogRoot(Transform source) {
			Transform current = source;
			Transform top = source;
			while (current != null) {
				top = current;
				if (string.Equals(current.name, "ModOptions", StringComparison.Ordinal))
					return current;
				current = current.parent;
			}
			return top;
		}

		private static GameObject FindNamedChild(Transform root, string name) {
			if (root == null) return null;
			Transform[] children = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; i++) {
				if (string.Equals(children[i].name, name, StringComparison.Ordinal))
					return children[i].gameObject;
			}
			return null;
		}
	}
}
