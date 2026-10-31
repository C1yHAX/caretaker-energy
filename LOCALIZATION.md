# Localization / Übersetzung

English is the native text; German translations are in `localization.json` and the bundled `translations.lua`. The runtime registers the entries with Unreal's native Polyglot localization manager once on the game thread. Existing FText values then follow game-language changes. Other cultures fall back to English. Stable namespace/key identities are preserved. No save migration is needed.

Englisch ist der native Text; deutsche Übersetzungen stehen in `localization.json` und der enthaltenen `translations.lua`. Die Registrierung erfolgt einmal im Spielthread über Unreals Polyglot-Sprachverwaltung. Bestehende FText-Werte folgen anschließend der Spielsprache. Andere Sprachen verwenden Englisch. Keine Spielstandmigration nötig.

For an existing extracted container, use `python tools/build_localization.py --source <extracted> --mapping <game.usmap> --donor <stock-transmitter.uasset> --retoc <retoc.exe> --container <container-name-without-extension> --output <new-empty-directory>`. The compatible UAssetAPI/Newtonsoft.Json/ZstdSharp DLLs belong in `tools/lib`. Only localized assets are replaced; all other source asset bytes are retained. Generate the Lua catalog with `generate_runtime` in that script, passing `localization.json` and the mod's Scripts directory. Package with `tools/package_release.py` after deliberately updating `Dateien.json`.

Native validation: 2026-10-31, game 0.10.0.653173, UE4SS 527a483b, AssetBridge 0.4.1, separate empty test profile. Checked en, de, en-US, de-DE, fr (English fallback), then repeated de/en on already-loaded texts. The original save and configuration files were hash-checked unchanged.
