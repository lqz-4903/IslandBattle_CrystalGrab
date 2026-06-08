-- JoinRoomPanel —— 加入房间面板
-- 流程：输入房间号/主机IP → 连接主机KCP → 发送JoinRoom → 处理JoinRoomAck → 进入CreateRoomPanel

BasePanel:subClass("JoinRoomPanel")

-- 面板名称（对应AB包中的预制体名称）
JoinRoomPanel.panelName = "JoinRoomPanel"

-- 单例引用
JoinRoomPanel.instance = nil

-- 加入流程状态
JoinRoomPanel._isJoining = false       -- 是否正在执行加入流程（防重复点击）
JoinRoomPanel._joinState = nil         -- nil / "connecting" / "sent" / "done"
JoinRoomPanel._joinElapsed = 0         -- 已等待时间
JoinRoomPanel._joinRoomId = nil        -- 当前尝试加入的房间号
JoinRoomPanel._joinPollId = nil        -- Update 轮询 ID

-- 默认端口（与服务端 HostServer 保持一致，可通过 inputPort 覆盖）
JoinRoomPanel._defaultPort = 8888

-- ═══════════════════════════════════════════════════════════════
--  Show / Hide
-- ═══════════════════════════════════════════════════════════════

function JoinRoomPanel:Show()
    if self.instance == nil then
        self.instance = self
    end
    self:ShowMe(self.panelName)
    if self.isInitEvent == false then
        self:BindEvents()
        self.isInitEvent = true
    end
    -- 重置状态
    self._isJoining = false
    self._joinState = nil
end

function JoinRoomPanel:Hide()
    -- 清理网络回调和轮询
    self:CleanupJoin()

    if self.panelObj ~= nil then
        self:StopFade()
        GameObject.Destroy(self.panelObj)
        self.panelObj = nil
        self.canvasGroup = nil
        self.controls = {}
        self.isInitEvent = false
    end
    self.instance = nil
end

-- ═══════════════════════════════════════════════════════════════
--  事件绑定
-- ═══════════════════════════════════════════════════════════════

function JoinRoomPanel:BindEvents()
    -- 返回按钮 → 回到 ChooseRoomPanel
    local btnBack = self:GetControl("btnBack", "Button")
    if btnBack ~= nil then
        btnBack.onClick:AddListener(function()
            self:Hide()
            ChooseRoomPanel:Show()
        end)
    end

    -- 确认加入按钮
    local btnJoin = self:GetControl("btnJoin", "Button")
    if btnJoin ~= nil then
        btnJoin.onClick:AddListener(function()
            if self._isJoining then
                TipPanel:Popup("正在加入房间，请稍候...")
                return
            end
            self:OnClickJoin()
        end)
    end
end

-- ═══════════════════════════════════════════════════════════════
--  加入逻辑
-- ═══════════════════════════════════════════════════════════════

-- 点击加入按钮
function JoinRoomPanel:OnClickJoin()
    -- 获取房间号
    local inputRoomNum = self:GetControl("inputRoomNum", "InputField")
    if inputRoomNum == nil then
        TipPanel:Popup("输入框未找到，请检查预制体")
        return
    end

    local roomNum = inputRoomNum.text
    if roomNum == nil or #roomNum == 0 then
        TipPanel:Popup("请输入房间号")
        return
    end

    -- 获取主机 IP（可选字段，默认 127.0.0.1 用于本机/局域网测试）
    local ip = "127.0.0.1"
    local inputIP = self:GetControl("inputIP", "InputField")
    if inputIP ~= nil and inputIP.text ~= nil and #inputIP.text > 0 then
        ip = inputIP.text
    end

    -- 获取端口（可选字段，默认 8888，与服务端一致）
    local port = self._defaultPort
    local inputPort = self:GetControl("inputPort", "InputField")
    if inputPort ~= nil and inputPort.text ~= nil and #inputPort.text > 0 then
        local parsedPort = tonumber(inputPort.text)
        if parsedPort ~= nil and parsedPort > 0 and parsedPort <= 65535 then
            port = parsedPort
        else
            TipPanel:Popup("端口号无效，请输入 1-65535 之间的数字")
            return
        end
    end

    local playerName = PlayerData.GetName()

    -- 弹出确认提示，显示完整连接信息
    TipPanel:Popup("连接 " .. ip .. ":" .. port .. " 加入房间 " .. roomNum .. " ？", function()
        self:ExecuteJoin(ip, port, roomNum, playerName)
    end)
end

-- 执行加入房间
function JoinRoomPanel:ExecuteJoin(ip, port, roomId, playerName)
    -- ═══════════════════════════════════════════════════════════
    --  路径1：本机测试（同一进程内，房主房间号直接比对）
    --  适用于开发阶段在本机同时运行主机和客户端
    -- ═══════════════════════════════════════════════════════════
    local hostRoomID = CreateRoomPanel.roomID
    if hostRoomID ~= nil and roomId == hostRoomID then
        -- 同一进程：直接以非房主身份进入房间面板
        -- 从已有的 txtRoomHost 文本中提取房主名（仅本机测试路径需要）
        local hostName = nil
        local txtRoomHost = CreateRoomPanel:GetControl("txtRoomHost", "Text")
        if txtRoomHost ~= nil and txtRoomHost.text ~= nil then
            local prefix = "房主："
            local startIdx = string.find(txtRoomHost.text, prefix)
            if startIdx ~= nil then
                hostName = string.sub(txtRoomHost.text, startIdx + #prefix)
            end
        end
        self:Hide()
        CreateRoomPanel:Show(false, roomId, hostName)
        return
    end

    -- ═══════════════════════════════════════════════════════════
    --  路径2：网络加入（远程主机）
    --  1. 注册 JoinRoomAck 回调（由 C# NetMgr 在收到响应时调用）
    --  2. 启动 KCP 客户端连接（含握手）
    --  3. 轮询等待握手完成 → 发送 JoinRoom 请求
    --  4. 回调中处理成功/失败
    -- ═══════════════════════════════════════════════════════════
    self._isJoining = true
    self._joinState = "connecting"
    self._joinElapsed = 0
    self._joinRoomId = roomId
    self._joinPlayerName = playerName

    -- 注册回调（通过 C# 静态 Action 桥接，避免 XLua 泛型委托转换问题）
    self:_registerAckCallback()

    -- 启动客户端连接（异步，fire-and-forget；结果通过 ClientConv 查询）
    CS.KcpMgr.Instance:StartAsClientAsync(ip, port)

    -- 启动轮询：等待 KCP 握手完成后发送 JoinRoom
    self._joinPollId = RegisterUpdate(function(dt)
        self:_pollJoinProgress(dt)
    end)
end

-- 轮询加入进度（由 RegisterUpdate 驱动）
function JoinRoomPanel:_pollJoinProgress(dt)
    self._joinElapsed = self._joinElapsed + dt

    -- 超时处理（最多等待 10 秒）
    if self._joinElapsed >= 10.0 then
        self:OnJoinFailed("连接主机超时，请检查IP地址和端口")
        return
    end

    -- 等待 KCP 握手完成（ClientConv 被赋值表示握手成功）
    if self._joinState == "connecting" then
        local clientConv = CS.KcpMgr.Instance.ClientConv
        if clientConv ~= 0 then
            -- 握手成功，发送 JoinRoom 请求
            print("【JoinRoomPanel】握手完成 ClientConv=" .. clientConv .. "，发送 JoinRoom")
            self._joinState = "sent"
            self:SendJoinRoomRequest(self._joinRoomId, self._joinPlayerName)
        end
    end

    -- "sent" 状态下等待 JoinRoomAck 回调（由 C# NetMgr 触发）
    -- 回调中会调用 OnJoinSuccess / OnJoinFailed，内部会 CleanupJoin
end

-- 发送 JoinRoom protobuf 消息
function JoinRoomPanel:SendJoinRoomRequest(roomId, playerName)
    print("【JoinRoomPanel】SendJoinRoomRequest roomId=" .. roomId .. " playerName=" .. playerName)
    local joinRoom = CS.GameProto.JoinRoom()
    joinRoom.RoomId = roomId
    joinRoom.PlayerName = playerName

    local netMsg = CS.GameProto.NetMessage()
    netMsg.JoinRoom = joinRoom

    print("【JoinRoomPanel】调用 NetMgr:Send")
    CS.NetMgr.Instance:Send(netMsg)
    print("【JoinRoomPanel】NetMgr:Send 返回")
end

-- ═══════════════════════════════════════════════════════════════
--  JoinRoomAck 回调（由 C# NetMgr.OnJoinRoomAckCallback 调用）
-- ═══════════════════════════════════════════════════════════════

-- 注册网络回调到 C# 侧
function JoinRoomPanel:_registerAckCallback()
    local selfRef = self
    CS.NetMgr.OnJoinRoomAckCallback = function(ack)
        if selfRef._joinState == "done" then
            return  -- 已处理过，忽略重复回调
        end
        if ack.Success then
            selfRef:OnJoinSuccess(ack)
        else
            selfRef:OnJoinFailed(ack.Error or "加入房间失败")
        end
    end
end

-- 加入成功
function JoinRoomPanel:OnJoinSuccess(ack)
    self._joinState = "done"

    -- 从 JoinRoomAck 中提取房间号和房主名
    -- ack.RoomId：服务端返回的房间号（权威来源）
    -- ack.Players：服务端返回的当前房间玩家列表（含 IsHost 标识）
    local roomId = ack.RoomId
    local hostName = nil
    if ack.Players ~= nil and ack.Players.Count > 0 then
        for i = 0, ack.Players.Count - 1 do
            local player = ack.Players[i]
            if player.IsHost then
                hostName = player.PlayerName
                break
            end
        end
    end

    -- 保存房间号到 CreateRoomPanel（用于本地路径匹配和后续使用）
    CreateRoomPanel.roomID = roomId

    self:CleanupJoin()

    self:Hide()
    CreateRoomPanel:Show(false, roomId, hostName)
end

-- 加入失败
function JoinRoomPanel:OnJoinFailed(errorMsg)
    self._joinState = "done"
    self:CleanupJoin()

    TipPanel:Popup(errorMsg)

    -- 断开 KCP 连接（异步 fire-and-forget）
    CS.KcpMgr.Instance:StopAsync()
end

-- 清理加入流程中的临时状态
function JoinRoomPanel:CleanupJoin()
    -- 移除 C# 回调
    CS.NetMgr.OnJoinRoomAckCallback = nil

    -- 停止轮询
    if self._joinPollId ~= nil then
        UnregisterUpdate(self._joinPollId)
        self._joinPollId = nil
    end

    self._isJoining = false
    self._joinElapsed = 0
    self._joinRoomId = nil
    self._joinPlayerName = nil
end
