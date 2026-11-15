-- LiveCG-EFF OBS controls | SunjooAn
local obs = obslua
local actions = {
    {id = "livecg.focus.next", label = "LiveCG: 다음 강조", command = "focusNext"},
    {id = "livecg.focus.previous", label = "LiveCG: 이전 강조", command = "focusPrevious"},
    {id = "livecg.focus.blur", label = "LiveCG: 전체 블러", command = "focusReset"},
    {id = "livecg.focus.home", label = "LiveCG: 처음부터 (원본)", command = "focusHome"}
}

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
    return "LiveCG-EFF OBS 단축키 v1.1 | SunjooAn<br>OBS 32·33용. 프로그램 화면에 활성화된 LiveCG 순차 블러 강조를 제어합니다.<br>설정 → 단축키에서 LiveCG 항목에 키를 한 번만 지정하세요."
end

local function on_press(command)
    return function(pressed)
        if pressed then send(command) end
    end
end

function script_load(settings)
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
