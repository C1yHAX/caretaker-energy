# Nachbauen

Benötigt: Python 3, .NET 10 SDK mit Windows-Desktop-Unterstützung, [retoc](https://github.com/trumank/retoc), [UAssetAPI](https://github.com/atenfyr/UAssetAPI) und eine eigene Installation des angegebenen Spielbuilds.

UAssetAPI-Basis: `21c982fa8f04e12d5d216fdf330a2f206e81156f`. Den Patch `tools/uassetapi-ue58-payload.patch` auf diese Fassung anwenden und Release bauen. `UAssetAPI.dll`, `Newtonsoft.Json.dll` und `ZstdSharp.dll` nach `tools/lib` kopieren. Diese Dateien werden nicht ins Repository eingecheckt. Die Anpassung trennt die von retoc geschriebenen UE5.6-Legacy-Header vom UE5.8-Payload des Spiels.

Aus den **unmodifizierten** Spielcontainern mit `retoc to-legacy --version UE5_6 --no-shaders` einen Legacy-Baum erzeugen. Dieser muss `scriptobjects.bin` und `Voyage/Content` enthalten. Benötigte Vorlagen sind:

- Blueprints: `BP_Module_DieselGenerator`, `BP_Module_PetrolGenerator_Portable`, `BP_Module_SmallGenerator`, `BP_Module_Generator_New`, einschließlich ihrer Blueprint-Abhängigkeiten.
- Items: `DA_Item_Module_DieselGenerator`, `DA_Item_Module_PortableElectricGenerator`, `DA_Item_Module_ElectricGenerator`.
- Skills: `DA_Skill_Engineering_Oil_Generator`, `DA_Skill_Engineering_Portable_Electric_Generator`, `DA_Skill_Engineering_Electric_Generator`.
- Stringtabelle: `ST_Modules_Generator`; referenzierte Socketdaten und weitere Schemas müssen lokal auflösbar sein.
- Materialinstanzen: die in `appearance.json` unter `source` genannten vier Originalmaterialien. Außerdem werden die beiden in `appearance.json` aufgeführten `T_WaterPurifier_Stationary_01/02_BaseColor`-Texturen einschließlich `.ubulk` benötigt. Deren Legacy-Baum wird mit `--appearance-stock` übergeben. Materialien und rote Farbtexturen erhalten eigene Paketpfade; Normalen-/Oberflächentexturen und Shader werden weiterhin aus dem Spiel geladen.

Das passende `.usmap` muss aus demselben Build stammen. Erwarteter SHA-256: `24ed679c5be228e6635939494db2f7682281250035d1980821b2a4a3e3d1e33d`.

```powershell
python build.py --mapping C:\TLC\Voyage.usmap --refinery-stock C:\TLC\legacy --generator-stock C:\TLC\legacy --appearance-stock C:\TLC\legacy --icon-stock C:\TLC\legacy --retoc C:\Tools\retoc.exe --output build-release
```

Der Ausgabeordner muss neu oder leer sein. Jeder Builder prüft erwartete Ausgangswerte, parst seine Ergebnisse erneut und verlangt einen verlustfreien Binärvergleich. Der AppearanceBuilder ergänzt sechs Materialinstanzen und zwei Farbtexturen und bindet die Materialien an die Gehäuse der fünf Geräte. Das Ergebnis unter `build-release/package` enthält 30 Assets in einem Container und zehn AssetBridge-Registrierungen. Der Build prüft den Container mit retoc und erstellt SHA-256-Prüfsummen. Zur unabhängigen Prüfung des Release wird zusätzlich gegen die Stock-Container zurückextrahiert und jeder Export-Payload bytegenau verglichen.

Der Build verändert weder Spielinstallation noch Spielstände. Die erzeugten Berichte enthalten lokale Pfade und gehören nicht in öffentliche Downloads.

Der IconBuilder benötigt die drei originalen UI-Texturen `Icon_Module_PortableGenerator`, `Icon_Module_Generator` und `Icon_Module_OilGenerator` unter `Voyage/Content/UI/Icons/Modules` im `--icon-stock`-Baum. Er verkleinert die fünf mitgelieferten PNG-Quellen auf 256×256, erhält Alpha, übernimmt das native BGRA8-UI-Layout und bindet Items und Skills an eigene Texturpfade. Der vollständige Build läuft unter Windows.
