# Localization / Übersetzung

English is the native text; German translations are in `localization.json` and the bundled `translations.lua`. The runtime registers the entries with Unreal's native Polyglot localization manager once on the game thread. Existing FText values then follow game-language changes. Other cultures fall back to English. Stable namespace/key identities are preserved. No save migration is needed.

Englisch ist der native Text; deutsche Übersetzungen stehen in `localization.json` und der enthaltenen `translations.lua`. Die Registrierung erfolgt einmal im Spielthread über Unreals Polyglot-Sprachverwaltung. Bestehende FText-Werte folgen anschließend der Spielsprache. Andere Sprachen verwenden Englisch. Keine Spielstandmigration nötig.

For Energy, use `python tools/build_localization.py --source <complete-extraction> --mapping <game.usmap> --donor <string-table.uasset> --retoc <retoc.exe> --container CaretakerEnergy_P --baseline-utoc <verified-working-container>/CaretakerEnergy_P.utoc --output <new-empty-directory>`. Extract with the original game containers and global container available together with Energy. The compatible UAssetAPI/Newtonsoft.Json/ZstdSharp DLLs belong in `tools/lib`. Missing `scriptobjects.bin` and unresolved `UnknownPackage`/`UnknownExport` imports now stop the build.

Energy converts only its two string tables, then `patch_energy_strings.py` replaces those two raw chunks in the verified working baseline. All other chunks, including the container header and dependencies, must remain byte-identical after packing and extracting again. The final package is in `preserved/package`. Generate the Lua catalog with `generate_runtime`, then package with `tools/package_release.py` after deliberately updating `Dateien.json`.

The October 31 build did not preserve gameplay data: its incomplete extraction had already lost original-game references before the source-file comparison. Passing text tests and a lossless read of those damaged files did not validate their dependencies. Version 1.0.1 restores the working data and verifies it in the game engine as well as in the final container.

Native validation: 2026-10-31, game 0.10.0.653173, UE4SS 527a483b, AssetBridge 0.4.1, separate empty test profile. Checked en, de, en-US, de-DE, fr (English fallback), then repeated de/en on already-loaded texts. The original save and configuration files were hash-checked unchanged.
