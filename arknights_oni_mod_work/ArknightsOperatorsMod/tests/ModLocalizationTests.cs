using System;
using ArknightsOperatorsMod;

public static class Localization {
	public static string GetCurrentLanguageCode() {
		return "en";
	}
}

namespace UnityEngine {
	public enum SystemLanguage {
		English,
		Chinese,
		ChineseSimplified,
		ChineseTraditional,
		Japanese
	}

	public static class Application {
		public static SystemLanguage systemLanguage = SystemLanguage.English;
	}
}

public static class ModLocalizationTests {
	private static int passed;

	public static int Main() {
		Expect(true, ModLocalization.IsChineseCode("zh_klei"), "ONI Chinese code");
		Expect(true, ModLocalization.IsChineseCode("zh-CN"), "BCP-47 Chinese code");
		Expect(true, ModLocalization.IsChineseCode("ChineseSimplified"), "Unity Chinese code");
		Expect(true, ModLocalization.IsChineseCode("schinese"), "Steam simplified Chinese code");
		Expect(false, ModLocalization.IsChineseCode("en"), "English code");
		Expect(false, ModLocalization.IsChineseCode("ko_klei"), "Korean code");
		Expect(false, ModLocalization.IsChineseCode(null), "missing code");
		Expect(true, ModLocalization.IsJapaneseCode("ja"), "Japanese language code");
		Expect(true, ModLocalization.IsJapaneseCode("ja-JP"), "Japanese BCP-47 code");
		Expect(false, ModLocalization.IsJapaneseCode("en"), "non-Japanese code");
		Console.WriteLine("ModLocalizationTests: " + passed + " passed");
		return 0;
	}

	private static void Expect(bool expected, bool actual, string name) {
		if (expected != actual)
			throw new Exception(name + ": expected " + expected + ", got " + actual);
		passed++;
	}
}
