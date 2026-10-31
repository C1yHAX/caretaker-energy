-- Register this mod's text with Unreal's native localization manager once.
-- FText instances then follow game culture changes without polling devices.
local M={}
function M.register(entries)
    local lib=StaticFindObject('/Script/Engine.Default__KismetTextLibrary')
    if not lib or not lib:IsValid() then return false end
    local object=StaticConstructObject(StaticFindObject('/Script/Engine.DataTable'),lib)
    if not object or not object:IsValid() then return false end
    -- StaticConstructObject returns a generic UObject; lookup supplies the
    -- UDataTable wrapper. Do not marshal a nested Lua map into PolyglotTextData:
    -- UE4SS 527a483b reads the wrong stack index for nested maps.
    local data=StaticFindObject(object:GetFullName():match('^%S+%s+(.+)$'))
    data.RowStruct=StaticFindObject('/Script/CoreUObject.PolyglotTextData')
    for i,entry in ipairs(entries) do
        local name=tostring(i)
        data:AddRow(name,{Category=0,NativeCulture='en',Namespace=entry.namespace,
            Key=entry.key,NativeString=entry.en,bIsMinimalPatch=false})
        local row=data:FindRow(name)
        row.LocalizedStrings:Add('de',entry.de)
        lib:PolyglotDataToText(row)
    end
    return true
end
function M.start(entries,label)
    local attempts=0
    local finished=false
    if not LoopInGameThreadWithDelay then
        print('['..label..'] Localization requires the game-thread scheduler.\n');return
    end
    LoopInGameThreadWithDelay(1000,function()
        if finished then return end
        local ok,ready=pcall(M.register,entries)
        if ok and ready then
            finished=true
            print('['..label..'] Registered '..#entries..' EN/DE texts; follows game language.\n')
            return true
        end
        if not ok then
            attempts=attempts+1
            print('['..label..'] Localization registration failed: '..tostring(ready)..'\n')
            if attempts>=3 then finished=true;return true end
        end
    end)
end
return M
