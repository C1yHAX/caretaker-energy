-- Development-only native reference regression test; never ship in the mod ZIP.
local C = assert(ENERGY_PROBE_CONFIG)
local T={ran=false,keep={}};_G.ENERGY_PROBE=T
function T.log(s)
    local f=assert(io.open(C.output..'/native-'..C.label..'.log','a'))
    f:write(s..'\n');f:close();print('[EnergyProbe] '..s..'\n')
end
function T.valid(o)return o and o:IsValid()end
function T.load(path)
    local sys=StaticFindObject('/Script/Engine.Default__KismetSystemLibrary')
    local o=sys:LoadAsset_Blocking(sys:Conv_SoftObjPathToSoftObjRef(sys:MakeSoftObjectPath(path)))
    if T.valid(o)then T.keep[#T.keep+1]=o end
    return o
end
function T.unwrap(v)local ok,o=pcall(function()return v:get()end);return ok and o or v end
function T.inspect()
    for _,row in ipairs(C.assets)do
        local ok,err=pcall(function()
            local item=T.load(row.path);assert(T.valid(item),'Asset missing')
            if row.kind=='Item'then
                local good,bad=0,0
                item.Components:ForEach(function(k,v)
                    local part,amount=T.unwrap(k),T.unwrap(v)
                    if T.valid(part)then good=good+1;T.log('PART '..row.name..' '..part:GetFName():ToString()..' '..tostring(amount))
                    else bad=bad+1;T.log('MISSING_PART '..row.name..' '..tostring(amount))end
                end)
                T.log('RECIPE '..row.name..' valid='..good..' missing='..bad..' category='..tostring(T.valid(item.CategoryAsset))..' icon='..tostring(T.valid(item.Icon)))
                local cls=T.load(row.blueprint);assert(T.valid(cls),'Blueprint class missing')
                T.log('CLASS '..row.name..' '..cls:GetFullName())
            else
                T.log('SKILL '..row.name..' '..item:GetFullName())
            end
        end)
        if not ok then T.log('FAIL '..row.name..' '..tostring(err))end
    end
    local found=0
    for _,component in ipairs(FindAllOf('StaticMeshComponent')or{})do
        local path=component:GetFullName()
        for _,row in ipairs(C.assets)do
            if row.kind=='Item'and path:find(row.blueprint:match('([^/.]+)_C$'),1,true)then
                found=found+1
                local mesh=component.StaticMesh
                T.log('MESH '..path..' = '..(T.valid(mesh)and mesh:GetFullName()or'MISSING'))
            end
        end
    end
    T.log('MESH_TEMPLATES '..found)
    T.log('REFERENCE_SCAN_DONE')
end
ExecuteInGameThreadWithDelay(20000,function()
    local ok,err=pcall(function()
        local sys=StaticFindObject('/Script/Engine.Default__KismetSystemLibrary')
        local saved=sys:GetProjectSavedDirectory():ToString():gsub('\\','/')
        assert(saved:find('energy-repair-20261104/profile-',1,true),'Wrong test profile: '..saved)
        T.log('ISOLATED_PROFILE '..saved)
        T.inspect()
    end)
    if not ok then T.log('FAIL '..tostring(err))end
end)
LoopInGameThreadWithDelay(1000,function()
    local path=C.output..'/command.lua';local f=io.open(path,'r')
    if not f then return end
    local source=f:read('*a');f:close();os.remove(path)
    local fn,e=load(source);if not fn then T.log('COMMAND_COMPILE_FAIL '..tostring(e));return end
    local ok,err=pcall(fn);T.log(ok and 'COMMAND_OK'or('COMMAND_FAIL '..tostring(err)))
end)
