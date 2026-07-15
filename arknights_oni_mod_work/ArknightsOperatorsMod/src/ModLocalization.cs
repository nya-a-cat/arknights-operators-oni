using System;
using UnityEngine;

namespace ArknightsOperatorsMod {
	internal static class ModLocalization {
		public static bool UseChinese {
			get {
				try {
					string code = Localization.GetCurrentLanguageCode();
					if (!string.IsNullOrWhiteSpace(code))
						return IsChineseCode(code);
				} catch {
					// Fall back to the system language if ONI has not initialized localization yet.
				}
				return Application.systemLanguage == SystemLanguage.ChineseSimplified ||
					Application.systemLanguage == SystemLanguage.ChineseTraditional ||
					Application.systemLanguage == SystemLanguage.Chinese;
			}
		}

		public static bool UseJapanese {
			get {
				try {
					string code = Localization.GetCurrentLanguageCode();
					if (!string.IsNullOrWhiteSpace(code))
						return IsJapaneseCode(code);
				} catch {
					// Fall back to the system language if ONI has not initialized localization yet.
				}
				return Application.systemLanguage == SystemLanguage.Japanese;
			}
		}

		public static string Text(string chinese, string english) {
			return UseChinese ? chinese : english;
		}

		internal static bool IsChineseCode(string code) {
			if (string.IsNullOrWhiteSpace(code)) return false;
			string normalized = code.Trim().Replace('-', '_').ToLowerInvariant();
			return normalized == "zh" || normalized.StartsWith("zh_", StringComparison.Ordinal) ||
				normalized.StartsWith("chinese", StringComparison.Ordinal) ||
				normalized == "schinese" || normalized == "tchinese";
		}

		internal static bool IsJapaneseCode(string code) {
			if (string.IsNullOrWhiteSpace(code)) return false;
			string normalized = code.Trim().Replace('-', '_').ToLowerInvariant();
			return normalized == "ja" || normalized.StartsWith("ja_", StringComparison.Ordinal) ||
				normalized.StartsWith("japanese", StringComparison.Ordinal);
		}
	}
}
