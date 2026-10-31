local folder=debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')
dofile(folder..'localization.lua').start(dofile(folder..'translations.lua'),'CaretakerEnergyLocalization')
