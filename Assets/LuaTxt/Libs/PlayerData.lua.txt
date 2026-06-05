-- 玩家数据持久化工具
-- 读写 persistentDataPath 下的 PlayerData.json
PlayerData = {}
PlayerData._data = nil
PlayerData._filePath = nil

-- 默认数据
PlayerData._defaults = {
    playerName = "Player",
    musicOn = true,
    soundOn = true,
    musicVolume = 1,
    soundVolume = 1
}

-- 获取文件路径（延迟初始化，确保CS可用）
function PlayerData._GetPath()
    if PlayerData._filePath == nil then
        PlayerData._filePath = CS.UnityEngine.Application.persistentDataPath .. "/PlayerData.json"
    end
    return PlayerData._filePath
end

-- 加载数据（首次调用时从文件读取，若文件不存在则创建默认数据）
function PlayerData.Load()
    if PlayerData._data ~= nil then
        return PlayerData._data
    end

    local path = PlayerData._GetPath()
    local content = nil

    -- 尝试读取文件
    local f = io.open(path, "r")
    if f then
        content = f:read("*a")
        f:close()
    end

    if content and #content > 0 then
        PlayerData._data = json.decode(content)
    end

    -- 文件不存在或解析失败，使用默认值
    if PlayerData._data == nil then
        PlayerData._data = {}
        for k, v in pairs(PlayerData._defaults) do
            PlayerData._data[k] = v
        end
        PlayerData.Save()
    else
        -- 补全缺失字段（版本兼容）
        for k, v in pairs(PlayerData._defaults) do
            if PlayerData._data[k] == nil then
                PlayerData._data[k] = v
            end
        end
    end

    return PlayerData._data
end

-- 保存数据到文件
function PlayerData.Save()
    if PlayerData._data == nil then return end

    local path = PlayerData._GetPath()
    local content = json.encode(PlayerData._data)
    local f = io.open(path, "w")
    if f then
        f:write(content)
        f:close()
    end
end

-- ========== 玩家名 ==========

function PlayerData.GetName()
    local data = PlayerData.Load()
    return data.playerName or "Player"
end

function PlayerData.SetName(name)
    local data = PlayerData.Load()
    data.playerName = name
    PlayerData.Save()
end

-- ========== 音乐设置 ==========

function PlayerData.GetMusicOn()
    local data = PlayerData.Load()
    return data.musicOn
end

function PlayerData.SetMusicOn(on)
    local data = PlayerData.Load()
    data.musicOn = on
end

function PlayerData.GetMusicVolume()
    local data = PlayerData.Load()
    return data.musicVolume
end

function PlayerData.SetMusicVolume(vol)
    local data = PlayerData.Load()
    data.musicVolume = vol
end

-- ========== 音效设置 ==========

function PlayerData.GetSoundOn()
    local data = PlayerData.Load()
    return data.soundOn
end

function PlayerData.SetSoundOn(on)
    local data = PlayerData.Load()
    data.soundOn = on
end

function PlayerData.GetSoundVolume()
    local data = PlayerData.Load()
    return data.soundVolume
end

function PlayerData.SetSoundVolume(vol)
    local data = PlayerData.Load()
    data.soundVolume = vol
end

return PlayerData
