using System;
using PeterHan.PLib.Options;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class AppearanceOptionsHotkey : MonoBehaviour {
		private bool dialogOpen;

		private void Update() {
			bool controlPressed = Input.GetKey(KeyCode.LeftControl) ||
				Input.GetKey(KeyCode.RightControl);
			if (dialogOpen || !controlPressed || !Input.GetKeyDown(KeyCode.F8)) return;
			try {
				dialogOpen = true;
				POptions.ShowDialog(typeof(ModConfig), OnDialogClosed);
			} catch (Exception error) {
				dialogOpen = false;
				Debug.LogError("[ArknightsOperatorsMod] Failed to open appearance options: " + error);
			}
		}

		private void OnDialogClosed(object settings) {
			dialogOpen = false;
			ModConfig config = settings as ModConfig;
			if (config == null) return;
			config.Normalize();
			ModConfigStore.ApplySaved(config);
		}
	}
}
