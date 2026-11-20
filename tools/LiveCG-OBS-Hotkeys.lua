-- LiveCG-EFF OBS controls | SunjooAn
local obs = obslua
local actions = {
    {id = "livecg.focus.next", label = "LiveCG: 다음 강조", command = "focusNext"},
    {id = "livecg.focus.previous", label = "LiveCG: 이전 강조", command = "focusPrevious"},
    {id = "livecg.focus.blur", label = "LiveCG: 전체 블러", command = "focusReset"},
    {id = "livecg.focus.home", label = "LiveCG: 처음부터 (원본)", command = "focusHome"}
}


-- Browser textures contain premultiplied sRGB. Default scene blending can darken
-- partially transparent edge pixels when Chromium resamples the canvas.
local fix_alpha = true
local ffi = require("ffi")
ffi.cdef[[
char *os_quick_read_utf8_file(const char *path);
void bfree(void *ptr);
]]
local native_obs = ffi.load("obs")

local function read_header(path)
    -- OBS uses UTF-8 paths. Lua's Windows io.open cannot read Korean filenames.
    local buffer = native_obs.os_quick_read_utf8_file(path)
    if buffer == nil then return "" end
    local header = ffi.string(buffer):sub(1, 1024)
    native_obs.bfree(buffer)
    return header
end

local function is_livecg(source, cache)
    if obs.obs_source_get_unversioned_id(source) ~= "browser_source" then return false end
    local settings = obs.obs_source_get_settings(source)
    local local_file = obs.obs_data_get_bool(settings, "is_local_file")
    local path = obs.obs_data_get_string(settings, "local_file")
    obs.obs_data_release(settings)
    if not local_file or path == "" then return false end
    if cache[path] ~= nil then return cache[path] end
    local header = read_header(path)
    local matches = header:find("LiveCG%-EFF") ~= nil or header:find("LiveCG linked source", 1, true) ~= nil
    cache[path] = matches
    return matches
end

local function fix_scene(scene, cache, visited)
    if not scene or visited[scene] then return end
    visited[scene] = true
    local items = obs.obs_scene_enum_items(scene)
    if not items then return end
    for _, item in ipairs(items) do
        local source = obs.obs_sceneitem_get_source(item)
        if is_livecg(source, cache) then
            if obs.obs_sceneitem_get_blending_method(item) ~= obs.OBS_BLEND_METHOD_SRGB_OFF then
                obs.obs_sceneitem_set_blending_method(item, obs.OBS_BLEND_METHOD_SRGB_OFF)
            end
        elseif obs.obs_sceneitem_is_group(item) then
            fix_scene(obs.obs_sceneitem_group_get_scene(item), cache, visited)
        else
            fix_scene(obs.obs_scene_from_source(source), cache, visited)
        end
    end
    obs.sceneitem_list_release(items)
end

local function repair_alpha()
    if not fix_alpha then return end
    local scenes = obs.obs_frontend_get_scenes()
    if not scenes then return end
    local cache, visited = {}, {}
    for _, source in ipairs(scenes) do
        fix_scene(obs.obs_scene_from_source(source), cache, visited)
    end
    obs.source_list_release(scenes)
end

function script_defaults(settings)
    obs.obs_data_set_default_bool(settings, "fix_alpha", true)
end

function script_update(settings)
    fix_alpha = obs.obs_data_get_bool(settings, "fix_alpha")
    repair_alpha()
end

function script_properties()
    local props = obs.obs_properties_create()
    obs.obs_properties_add_bool(props, "fix_alpha", "LiveCG 투명 경계 자동 보정")
    obs.obs_properties_add_button(props, "repair_alpha", "지금 보정 적용", function()
        repair_alpha()
        return true
    end)
    return props
end

function script_unload()
    obs.timer_remove(repair_alpha)
end

local function send(command)
    local sources = obs.obs_enum_sources()
    if not sources then return end

    local call = obs.calldata_create()
    obs.calldata_set_string(call, "eventName", "LiveCGControl")
    obs.calldata_set_string(call, "jsonString", '{"command":"' .. command .. '","onProgram":true}')
    for _, source in ipairs(sources) do
        if obs.obs_source_get_unversioned_id(source) == "browser_source" and obs.obs_source_active(source) then
            obs.proc_handler_call(obs.obs_source_get_proc_handler(source), "javascript_event", call)
        end
    end
    obs.calldata_destroy(call)
    obs.source_list_release(sources)
end

function script_description()
    return "LiveCG-EFF OBS 단축키 v1.2 | SunjooAn<br>OBS 32·33용. 프로그램 화면에 활성화된 LiveCG 순차 블러 강조를 제어합니다.<br>설정 → 단축키에서 LiveCG 항목에 키를 한 번만 지정하세요.<br>LiveCG 투명 경계를 자동 보정합니다 (앱 v1.5.11)."
end

local function on_press(command)
    return function(pressed)
        if pressed then send(command) end
    end
end

function script_load(settings)
    script_update(settings)
    obs.timer_add(repair_alpha, 1000)
    for _, action in ipairs(actions) do
        action.hotkey = obs.obs_hotkey_register_frontend(action.id, action.label, on_press(action.command))
        local keys = obs.obs_data_get_array(settings, action.id)
        if keys then
            obs.obs_hotkey_load(action.hotkey, keys)
            obs.obs_data_array_release(keys)
        end
    end
end

function script_save(settings)
    for _, action in ipairs(actions) do
        local keys = obs.obs_hotkey_save(action.hotkey)
        obs.obs_data_set_array(settings, action.id, keys)
        obs.obs_data_array_release(keys)
    end
end
