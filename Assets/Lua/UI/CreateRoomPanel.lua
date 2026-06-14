-- 继承BasePanel
BasePanel:subClass("CreateRoomPanel")

-- 面板名称（对应AB包中的预制体名称）
CreateRoomPanel.panelName = "CreateRoomPanel"

-- 单例引用
CreateRoomPanel.instance = nil

-- 开始游戏按钮引用（用于置灰控制）
CreateRoomPanel.btnStartGame = nil
-- 是否为房主
CreateRoomPanel.isHost = true
-- 房间号（房主创建时由服务端生成，加入者输入时匹配用）
CreateRoomPanel.roomID = nil
-- 主机端口（默认 8888，可通过 inputPort 覆盖）
CreateRoomPanel._hostPort = 8888
-- 是否已注册过 PlayerList 回调
CreateRoomPanel._playerListRegistered = false
-- 是否已注册过 KickOff 回调（仅用于房间解散通知）
CreateRoomPanel._kickOffRegistered = false
-- 是否已注册过 PlayerOffline 回调（用于心跳超时离线通知）
CreateRoomPanel._playerOfflineRegistered = false
-- 是否已注册过 GameStart 回调（仅非房主）
CreateRoomPanel._gameStartRegistered = false
-- 加入时的房主名（游戏结束后回到房间时用于恢复显示）
CreateRoomPanel._joinHostName = nil

-- 显示面板
-- isHost:       true=房主（默认），false=加入者
-- roomId:       加入时由 JoinRoomPanel 传入的房间号（网络路径从 JoinRoomAck 获取）
-- joinHostName: 加入时由 JoinRoomPanel 传入的房主名（从 JoinRoomAck.Players 中查找 IsHost）
function CreateRoomPanel:Show(isHost, roomId, joinHostName)
    if self.instance == nil then
        self.instance = self
    end
    -- ★ 只有显式传入 isHost 时才覆盖，否则保留之前的状态（支持游戏结束后回到房间）
    if isHost ~= nil then
        self.isHost = isHost
    end
    -- 如果 roomId 被显式传入，更新房间号
    if roomId ~= nil then
        self.roomID = roomId
    end
    -- 保存加入时的房主名（游戏结束后回到房间时用于恢复显示）
    if joinHostName ~= nil then
        self._joinHostName = joinHostName
    end

    self:ShowMe(self.panelName)
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end

    -- ★ 注册 PlayerList 回调（房主和加入者都需要）
    self:_registerPlayerListCallback()
    -- ★ 注册 PlayerOffline 回调（房主和加入者都需要——接收其他玩家心跳超时离线通知）
    self:_registerPlayerOfflineCallback()
    -- ★ 注册 KickOff 回调（仅非房主需要——接收服务器解散通知）
    -- ★ 注册 GameStart 回调（仅非房主需要——接收服务器开始游戏通知）
    if not self.isHost then
        self:_registerKickOffCallback()
        self:_registerGameStartCallback()
    end

    if self.isHost then
        -- 房主：启动 KCP 服务器（如果尚未运行），生成/复用房间号，添加自己为第一个玩家

        -- 读取可选端口配置（预制体有 inputPort 则使用，否则用默认 8888）
        local inputPort = self:GetControl("inputPort", "InputField")
        if inputPort ~= nil and inputPort.text ~= nil and #inputPort.text > 0 then
            local parsedPort = tonumber(inputPort.text)
            if parsedPort ~= nil and parsedPort > 0 and parsedPort <= 65535 then
                self._hostPort = parsedPort
            end
        end

        -- 确保 HostServer 存在
        if CS.HostServer.Instance == nil then
            local go = CS.UnityEngine.GameObject("HostServer")
            go:AddComponent(typeof(CS.HostServer))
            CS.UnityEngine.Object.DontDestroyOnLoad(go)
        end

        -- ★ 立即启动 KCP 服务器（如果尚未运行），让客户端可以在房间阶段就连接
        if not CS.HostServer.Instance.IsRunning then
            local hostName = PlayerData.GetName()
            CS.HostServer.Instance:StartHost(hostName, self._hostPort)
        end

        -- ★ 从服务端读取真实房间号（由 CreateLocalRoom 生成），
        --    不能用 GenerateRoomId() 再生成一次，否则和服务器验证的 RoomId 不一致
        if self.roomID == nil then
            self.roomID = CS.HostServer.Instance:GetCurrentRoomId()
        end
        -- 房间号 → txtRoomID
        local txtRoomID = self:GetControl("txtRoomID", "Text")
        if txtRoomID ~= nil then
            txtRoomID.text = "房间号：" .. self.roomID .. "  端口：" .. self._hostPort
        end
        -- 房主名 → txtRoomHost
        local hostName = PlayerData.GetName()
        local txtRoomHost = self:GetControl("txtRoomHost", "Text")
        if txtRoomHost ~= nil then
            txtRoomHost.text = "房主：" .. hostName
        end

        -- ★ 保存本地玩家信息（供 Main.lua 游戏启动时使用）
        _G.localPlayerId = 1   -- 房主始终是 playerId=1
        _G.localPlayerName = hostName

        -- ★ 如果服务器已在运行（游戏结束后回到房间），清空本地 UI 并从服务器刷新完整玩家列表
        --    如果服务器未运行（首次创建房间），只添加房主自己
        if CS.HostServer.Instance.IsRunning and CS.HostServer.Instance.CurrentRoom ~= nil then
            self:_clearPlayers()
            CS.HostServer.Instance:RefreshPlayerList()
        else
            self:AddPlayer(hostName)
        end
    else
        -- 加入者：更新房间号和房主信息
        local txtRoomID = self:GetControl("txtRoomID", "Text")
        if txtRoomID ~= nil and self.roomID ~= nil then
            txtRoomID.text = "房间号：" .. self.roomID
        end
        if self._joinHostName ~= nil then
            local txtRoomHost = self:GetControl("txtRoomHost", "Text")
            if txtRoomHost ~= nil then
                txtRoomHost.text = "房主：" .. self._joinHostName
            end
        end

        -- 加入者：隐藏开始按钮，解散按钮改为"离开房间"
        if self.btnStartGame ~= nil then
            self.btnStartGame.gameObject:SetActive(false)
        end
        local btnDisband = self:GetControl("btnDisband", "Button")
        if btnDisband ~= nil then
            -- 修改子对象 txtDisband 的文字
            local txtDisband = btnDisband.transform:Find("txtDisband")
            if txtDisband ~= nil then
                local txt = txtDisband:GetComponent(typeof(CS.UnityEngine.UI.Text))
                if txt ~= nil then
                    txt.text = "离开房间"
                end
            end
        end

        -- ★ 客户端请求服务器刷新玩家列表（确保游戏结束后回到房间时能获取最新状态）
        CS.NetMgr.RequestPlayerListRefresh()
    end
end

-- ═══════════════════════════════════════════════════════════════
--  PlayerList 回调（C# NetMgr.OnPlayerListCallback → Lua）
-- ═══════════════════════════════════════════════════════════════

function CreateRoomPanel:_registerPlayerListCallback()
    if self._playerListRegistered then
        return
    end
    self._playerListRegistered = true

    local selfRef = self
    CS.NetMgr.OnPlayerListCallback = function(playerList)
        if selfRef.panelObj == nil then
            return  -- 面板已销毁，忽略
        end
        selfRef:_onPlayerListReceived(playerList)
    end
end

-- 收到服务器发来的 PlayerList，增量更新玩家列表 UI（智能 diff）
-- 服务器是权威数据源，按名字匹配：
--   - 离开的玩家 → 移除其 txtPlayer
--   - 新加入的玩家 → 追加 txtPlayer
--   - 已有的玩家 → 更新状态文字（游戏结束后显示"还在游戏中"/"已在房间"）
-- ★ 状态编码协议：服务器在"还在游戏中"的玩家名字末尾追加 \x01 字符，
--    Lua 解析时去掉 \x01，并根据是否存在来判断状态（客户端没有 HostServer 也能工作）
function CreateRoomPanel:_onPlayerListReceived(playerList)
    if playerList.Players == nil then return end

    -- ★ 保存最新 PlayerList 到全局变量（供 Main.lua InitGame 生成玩家用）
    _G.lastPlayerList = playerList

    -- 1. 按服务端顺序收集玩家（protobuf 列表顺序 = 加入顺序，房主在第一位）
    local serverOrdered = {}       -- 数组：{cleanName1, cleanName2, ...} 保持服务端顺序
    local serverNameToStatus = {}  -- 干净名字 → "已在房间" / "还在游戏中"
    for i = 0, playerList.Players.Count - 1 do
        local p = playerList.Players[i]
        local rawName = p.PlayerName
        local cleanName = rawName
        local isInGame = false
        if string.sub(rawName, -1) == "\x01" then
            cleanName = string.sub(rawName, 1, -2)
            isInGame = true
        end
        serverOrdered[i + 1] = cleanName
        serverNameToStatus[cleanName] = isInGame and "还在游戏中" or "已在房间"
    end

    -- 构建快速查找表
    local serverLookup = {}
    for _, name in ipairs(serverOrdered) do
        serverLookup[name] = true
    end

    -- 2. 移除 UI 中不在服务端列表的玩家条目
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    local existingNames = {}
    local toRemove = {}
    if imgPeoples ~= nil then
        local transform = imgPeoples.transform
        for i = 0, transform.childCount - 1 do
            local child = transform:GetChild(i)
            if string.find(child.name, "txtPlayer") ~= nil then
                local txt = child:GetComponent(typeof(CS.UnityEngine.UI.Text))
                if txt ~= nil then
                    local parts = txt.text:split("\t")
                    if #parts >= 2 then
                        local name = parts[2]
                        if serverLookup[name] then
                            existingNames[name] = child  -- 保留，记录引用
                        else
                            table.insert(toRemove, child)
                        end
                    end
                end
            end
        end
    end
    for _, child in ipairs(toRemove) do
        child:SetParent(nil)
        GameObject.Destroy(child.gameObject)
    end

    -- 3. 按服务端顺序添加 UI 中没有的玩家
    local added = false
    for _, cleanName in ipairs(serverOrdered) do
        if not existingNames[cleanName] then
            self:AddPlayer(cleanName)
            added = true
        end
    end

    -- 4. ★ 按服务端顺序重排所有 txtPlayer 的层级（sibling index）
    self:_ReorderPlayersToServerOrder(serverOrdered)

    -- 5. 更新所有现有玩家的状态文字
    self:_refreshAllPlayerStatus(serverNameToStatus)

    -- 6. 如果有增删，刷新布局和按钮状态
    if #toRemove > 0 or added then
        self:LayoutPlayers()
        self:RefreshStartBtn()
    end
end

-- 清空 imgPeoples 下的所有 txtPlayer 子对象
function CreateRoomPanel:_clearPlayers()
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local transform = imgPeoples.transform
    local toRemove = {}
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            table.insert(toRemove, child)
        end
    end
    for _, child in ipairs(toRemove) do
        child:SetParent(nil)
        GameObject.Destroy(child.gameObject)
    end
end

-- 添加一个玩家到imgPeoples下
function CreateRoomPanel:AddPlayer(playerName)
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local parentRect = imgPeoples:GetComponent(typeof(CS.UnityEngine.RectTransform))
    if parentRect == nil then return end

    -- 加载txtPlayer预制体并实例化到imgPeoples下
    local txtPlayerObj = ABMgr:LoadRes("ui", "txtPlayer", typeof(GameObject))
    if txtPlayerObj ~= nil then
        txtPlayerObj.transform:SetParent(imgPeoples.transform, false)

        -- 设置文字内容
        local txt = txtPlayerObj:GetComponent(typeof(CS.UnityEngine.UI.Text))
        if txt ~= nil then
            txt.text = "玩家\t" .. playerName .. "\t已在房间"
        end
    end

    -- 重新计算所有txtPlayer的布局
    self:LayoutPlayers()

    -- 刷新开始按钮状态
    self:RefreshStartBtn()
end

-- 刷新所有 txtPlayer 子对象的状态文字（游戏结束后调用）
-- serverNameToStatus: { 干净名字 → "已在房间" / "还在游戏中" } 映射表
-- ★ 状态来自服务器编码在 PlayerName 末尾的 \x01，无需 HostServer 查询
function CreateRoomPanel:_refreshAllPlayerStatus(serverNameToStatus)
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local transform = imgPeoples.transform
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            local txt = child:GetComponent(typeof(CS.UnityEngine.UI.Text))
            if txt ~= nil then
                local parts = txt.text:split("\t")
                if #parts >= 2 then
                    local name = parts[2]
                    local status = serverNameToStatus[name]
                    if status ~= nil then
                        txt.text = "玩家\t" .. name .. "\t" .. status
                    end
                end
            end
        end
    end
end

-- 按服务端顺序重排所有 txtPlayer 在 imgPeoples 下的层级
-- serverOrdered: 服务端返回的有序名字数组 {name1, name2, ...}
function CreateRoomPanel:_ReorderPlayersToServerOrder(serverOrdered)
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local transform = imgPeoples.transform

    -- 收集所有 txtPlayer 子对象，建立 name → child 映射
    local nameToChild = {}
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            local txt = child:GetComponent(typeof(CS.UnityEngine.UI.Text))
            if txt ~= nil then
                local parts = txt.text:split("\t")
                if #parts >= 2 then
                    nameToChild[parts[2]] = child
                end
            end
        end
    end

    -- 按服务端顺序逐次 setAsLastSibling，最终顺序 = serverOrdered
    -- 先把所有 txtPlayer 移到末尾（按 serverOrdered 顺序），非 txtPlayer 保持在前面
    -- 技巧：按 serverOrdered 反序 setAsFirstSibling
    for i = #serverOrdered, 1, -1 do
        local name = serverOrdered[i]
        local child = nameToChild[name]
        if child ~= nil then
            child:SetAsFirstSibling()
        end
    end
end

-- 重新计算imgPeoples下所有txtPlayer的布局
-- <4个：从顶部排列；>=4个：整体居中
function CreateRoomPanel:LayoutPlayers()
    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then return end

    local parentRect = imgPeoples:GetComponent(typeof(CS.UnityEngine.RectTransform))
    if parentRect == nil then return end

    local spacing = 85
    local itemHeight = 60
    local parentWidth = parentRect.rect.width
    local parentHeight = parentRect.rect.height
    local padding = parentWidth * 0.05

    -- 统计txtPlayer子对象
    local players = {}
    local transform = imgPeoples.transform
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            table.insert(players, child)
        end
    end

    local count = #players
    if count == 0 then return end

    -- 计算起始Y：>=4个居中，<4个从顶部留间距开始
    local totalSpan = (count - 1) * spacing + itemHeight
    local topPadding = 50
    local startY = topPadding
    if count >= 4 then
        startY = (parentHeight - totalSpan) / 2
    end

    -- 设置每个txtPlayer的位置
    for i, player in ipairs(players) do
        local rect = player:GetComponent(typeof(CS.UnityEngine.RectTransform))
        if rect ~= nil then
            rect.anchorMin = CS.UnityEngine.Vector2(0, 1)
            rect.anchorMax = CS.UnityEngine.Vector2(1, 1)
            rect.pivot = CS.UnityEngine.Vector2(0.5, 1)

            local index = i - 1
            rect.offsetMin = CS.UnityEngine.Vector2(padding, -(startY + index * spacing + itemHeight))
            rect.offsetMax = CS.UnityEngine.Vector2(-padding, -(startY + index * spacing))
        end
    end
end

-- 隐藏并销毁面板
function CreateRoomPanel:Hide()
    -- 移除回调
    self:_unregisterPlayerListCallback()
    self:_unregisterKickOffCallback()
    self:_unregisterPlayerOfflineCallback()
    self:_unregisterGameStartCallback()

    self:DestroyPanel()
    self.btnStartGame = nil
    self.instance = nil
end

-- 注销 PlayerList 回调
function CreateRoomPanel:_unregisterPlayerListCallback()
    if not self._playerListRegistered then
        return
    end
    self._playerListRegistered = false
    -- 只有当前实例才清除回调（避免其他面板注册的回调被误清）
    CS.NetMgr.OnPlayerListCallback = nil
end

-- ═══════════════════════════════════════════════════════════════
--  KickOff 回调（服务器解散房间 → 通知客户端）
-- ═══════════════════════════════════════════════════════════════

function CreateRoomPanel:_registerKickOffCallback()
    if self._kickOffRegistered then
        return
    end
    self._kickOffRegistered = true

    local selfRef = self
    CS.NetMgr.OnKickOffCallback = function(kickOff)
        print("【CreateRoomPanel】收到 KickOff reason=" .. (kickOff.Reason or "nil"))
        if selfRef.panelObj == nil then
            return
        end
        selfRef:_onKickOffReceived(kickOff)
    end
end

function CreateRoomPanel:_unregisterKickOffCallback()
    if not self._kickOffRegistered then
        return
    end
    self._kickOffRegistered = false
    CS.NetMgr.OnKickOffCallback = nil
end

-- 收到 KickOff（仅用于房间解散通知 —— 不再承载其他内部协议）
function CreateRoomPanel:_onKickOffReceived(kickOff)
    local reason = kickOff.Reason or ""
    print("【CreateRoomPanel】房间已被解散，返回 ChooseRoomPanel")
    TipPanel:Popup(reason ~= "" and reason or "房间已解散", function()
        -- 断开 KCP（客户端模式）
        if CS.KcpMgr.Instance.ClientConv ~= 0 then
            CS.KcpMgr.Instance:StopAsync()
        end
        self:Hide()
        ChooseRoomPanel:Show()
    end)
end

-- ═══════════════════════════════════════════════════════════════
--  PlayerOffline 回调（服务器通知：某玩家心跳超时离线）
-- ═══════════════════════════════════════════════════════════════

function CreateRoomPanel:_registerPlayerOfflineCallback()
    if self._playerOfflineRegistered then
        return
    end
    self._playerOfflineRegistered = true

    local selfRef = self
    CS.NetMgr.OnPlayerOfflineCallback = function(playerOffline)
        print("【CreateRoomPanel】收到 PlayerOffline playerId=" .. (playerOffline.PlayerId or 0) .. " name=" .. (playerOffline.PlayerName or "nil"))
        if selfRef.panelObj == nil then
            return
        end
        selfRef:_onPlayerOfflineReceived(playerOffline)
    end
end

function CreateRoomPanel:_unregisterPlayerOfflineCallback()
    if not self._playerOfflineRegistered then
        return
    end
    self._playerOfflineRegistered = false
    CS.NetMgr.OnPlayerOfflineCallback = nil
end

-- 收到 PlayerOffline（其他玩家心跳超时离线）
function CreateRoomPanel:_onPlayerOfflineReceived(playerOffline)
    local playerName = playerOffline.PlayerName or "未知玩家"
    print("【CreateRoomPanel】玩家离线通知：" .. playerName)
    -- 弹出提示但不离开房间（PlayerList 更新会自动清除 txtPlayer 并重新布局）
    local selfRef = self
    TipPanel:Popup("玩家 " .. playerName .. " 已离线，已被移出房间", function()
        -- ★ 保险：TipPanel 关闭后再次检查按钮状态
        --    Unity Destroy 是延迟销毁，此时已过至少一帧，层级已更新
        if selfRef.panelObj ~= nil then
            selfRef:RefreshStartBtn()
        end
    end)
end

-- ═══════════════════════════════════════════════════════════════
--  GameStart 回调（仅非房主客户端——接收服务器开始游戏通知）
-- ═══════════════════════════════════════════════════════════════

function CreateRoomPanel:_registerGameStartCallback()
    if self._gameStartRegistered then
        return
    end
    self._gameStartRegistered = true

    local selfRef = self
    CS.NetMgr.OnGameStartCallback = function(gameStart)
        print("【CreateRoomPanel】客户端收到 GameStart seed=" .. gameStart.RandomSeed)
        if selfRef.panelObj == nil then
            return
        end
        selfRef:_onGameStartReceived(gameStart)
    end
end

function CreateRoomPanel:_unregisterGameStartCallback()
    if not self._gameStartRegistered then
        return
    end
    self._gameStartRegistered = false
    CS.NetMgr.OnGameStartCallback = nil
end

-- 收到 GameStart（服务器已开始游戏，客户端切换到 GameScene）
function CreateRoomPanel:_onGameStartReceived(gameStart)
    print("【CreateRoomPanel】服务器开始游戏，客户端切换到 GameScene seed=" .. gameStart.RandomSeed)

    -- ★ 保存 GameStart 数据供 Main.lua InitGame 使用
    _G.pendingGameStart = {
        randomSeed   = gameStart.RandomSeed,
        playerCount  = gameStart.PlayerCount,
        gameDuration = gameStart.GameDuration,
        targetScore  = gameStart.TargetScore,
        tickRate     = gameStart.TickRate,
    }

    -- 销毁所有UI，切换到GameScene（与房主 btnStartGame 行为一致）
    self:Hide()
    BeginPanel:Hide()
    BeginBKPanel:Hide()
    CS.SceneMgr.Instance:LoadScene("GameScene")
end

-- 检查imgPeoples下txtPlayer子对象数量，>=2才能开始游戏
function CreateRoomPanel:RefreshStartBtn()
    if self.btnStartGame == nil then return end

    local imgPeoples = self:GetControl("imgPeoples", "Image")
    if imgPeoples == nil then
        self.btnStartGame.interactable = false
        return
    end

    -- 遍历imgPeoples的子对象，统计txtPlayer数量
    local count = 0
    local transform = imgPeoples.transform
    for i = 0, transform.childCount - 1 do
        local child = transform:GetChild(i)
        if string.find(child.name, "txtPlayer") ~= nil then
            count = count + 1
        end
    end

    self.btnStartGame.interactable = (count >= 2)
end

-- 绑定所有按钮事件
function CreateRoomPanel:BindEvents()
    -- 解散/离开按钮：房主解散房间，加入者离开房间
    local btnDisband = self:GetControl("btnDisband", "Button")
    if btnDisband ~= nil then
        btnDisband.onClick:AddListener(function()
            if self.isHost then
                -- 房主：先广播 KickOff 给所有客户端，再关闭服务器
                self.roomID = nil
                print("【CreateRoomPanel】房主解散房间，广播 KickOff...")
                if CS.HostServer.Instance ~= nil then
                    CS.HostServer.Instance:BroadcastKickOffAndShutdown("房间已解散")
                end
            elseif CS.KcpMgr.Instance.ClientConv ~= 0 then
                -- 远程客户端：先发 KickOff 通知服务器，延迟后断开 KCP
                print("【CreateRoomPanel】客户端离开房间，发送 KickOff...")
                CS.NetMgr.Instance:SendKickOffAndStop("玩家主动离开")
            else
                -- 本机测试模式（没有走 KCP 连接），直接关闭
                print("【CreateRoomPanel】本机模式，直接离开房间")
            end
            self:Hide()
            ChooseRoomPanel:Show()
        end)
    end

    -- 开始游戏按钮
    self.btnStartGame = self:GetControl("btnStartGame", "Button")
    if self.btnStartGame ~= nil then
        self.btnStartGame.onClick:AddListener(function()
            -- 弹出确认提示
            TipPanel:Popup("你确定开始游戏吗", function()
                -- 如果 HostServer 不存在，动态创建（兜底）
                if CS.HostServer.Instance == nil then
                    local go = CS.UnityEngine.GameObject("HostServer")
                    go:AddComponent(typeof(CS.HostServer))
                    CS.UnityEngine.Object.DontDestroyOnLoad(go)
                end
                -- 使用确定性随机生成种子，保证跨平台一致
                local rng = DeterministicRandom.new(os.time())
                local seed = rng:nextRange(1, 2147483647)

                -- 统计当前房间玩家数
                local count = 0
                local imgPeoples = self:GetControl("imgPeoples", "Image")
                if imgPeoples ~= nil then
                    local t = imgPeoples.transform
                    for i = 0, t.childCount - 1 do
                        if string.find(t:GetChild(i).name, "txtPlayer") ~= nil then
                            count = count + 1
                        end
                    end
                end

                -- ★ 保存 GameStart 数据供 Main.lua InitGame 使用（房主路径）
                _G.pendingGameStart = {
                    randomSeed   = seed,
                    playerCount  = count,
                    gameDuration = 120,
                    targetScore  = 10,
                    tickRate     = 30,
                }

                -- 从已有房间启动游戏（KCP 服务器已在 Show 时启动，不重启）
                CS.HostServer.Instance:StartGame(seed, 120)
                -- 销毁所有UI，切换到GameScene
                self:Hide()
                BeginPanel:Hide()
                BeginBKPanel:Hide()
                CS.SceneMgr.Instance:LoadScene("GameScene")
            end)
        end)
    end
end
