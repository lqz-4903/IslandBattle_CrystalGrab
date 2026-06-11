# CLAUDE.md

## 项目概述

**IslandBattle_CrystalGrab** — 基于 Unity + XLua 的多人联机对战游戏 Demo。

- **玩法**：2-4 名玩家在岛屿场景中对抗，拾取水晶得分，目标 10 分获胜，限时 120 秒。
- **网络模型**：Client-Server 帧同步（30fps 逻辑帧），房主即服务器（HostServer），通过 KCP over UDP 通信。
- **权威模式**：服务端权威（主机执行确定性物理，广播 `InputTick` 附带权威位置），客户端做 60fps 预测 + 平滑插值 + 硬回滚校正。
- **仓库**：`git@github.com:lqz-4903/IslandBattle_CrystalGrab.git`

---

## 技术栈

| 层 | 技术 | 说明 |
|---|---|---|
| 引擎 | Unity (C#) | 渲染、物理（CharacterController）、场景管理 |
| 热更新 / 业务逻辑 | **XLua** | 所有游戏逻辑用 Lua 编写，C# 提供底层基础设施 |
| 网络传输 | **KCP over UDP** | `KcpMgr` 封装，多线程收发（UdpRecv / KcpSend / KcpUpdate） |
| 序列化 | **Protobuf3** | `Protobuf/proto/game.proto` → `Protobuf/csharp/Game.cs` |
| 确定性数学 | **Fix64** | 定点数运算，确保客户端/服务端物理结果一致 |
| 资源加载 | **AssetBundle** | `ABMgr` 管理 AB 包加载（`ABRes/`） |

---

## 目录结构

```
Assets/
├── Lua/                        # ★ 业务逻辑（XLua 热更新）
│   ├── Main.lua                # 入口：场景切换、游戏初始化
│   ├── InitClass.lua           # package.path 配置、全局 Update 驱动
│   ├── Battle/
│   │   ├── InputHandler.lua    # 键盘鼠标输入采集（60fps，WASD + 鼠标视角）
│   │   └── PlayerController.lua # 本地玩家：移动预测、摄像机、输入提交、攻击/交互
│   ├── Core/
│   │   ├── GameConst.lua       # 常量（速度/重力/帧率/按键/网络消息ID）
│   │   ├── PlayerEntity.lua    # 玩家数据实体（位置/HP/分数/输入状态）
│   │   ├── PlayerManager.lua   # 玩家管理：生成/帧同步执行/插值/权威位置校正
│   │   └── NetworkEventMgr.lua # 网络事件监听分发（水晶/受击/坠落/重连）
│   ├── Libs/                   # Lua 工具库（Fix64/Object/Json 等）
│   └── UI/                     # 12 个 UI 面板（BeginPanel/CreateRoom/GamePanel/GameOver...）
├── Scripts/
│   ├── GameMgr.cs              # 总入口 MonoBehaviour：驱动 XLua、场景加载回调
│   ├── Net/
│   │   ├── KcpMgr.cs           # KCP UDP 封装（握手/收发线程/多conv管理）
│   │   └── NetMgr.cs           # 网络消息管理器：Protobuf 解析、Lua 回调桥接、心跳
│   ├── NetLogic/
│   │   ├── HostServer.cs       # 主机服务器协调器（RoomHandler + TickSync + GameEvent）
│   │   ├── RoomHandler.cs      # 房间管理（创建/加入/开始）
│   │   ├── TickSyncHandler.cs  # 帧同步核心（30fps，收集输入→组装InputTick→广播）
│   │   ├── TickExecutor.cs     # 帧执行器（驱动 Lua 侧帧逻辑）
│   │   └── GameEventHandler.cs # 游戏事件（水晶/受击/坠落/结算）
│   ├── Core/
│   │   ├── FSLibs/Fix64.cs     # 定点数 (16.16)，确定性运算
│   │   ├── FSLibs/Fix64Vector3.cs
│   │   ├── CameraMgr.cs
│   │   ├── EventCenter/        # C# 事件中心（二参/三参数字典，支持 conv 路由）
│   │   └── LuaEventBridge.cs   # C# → Lua 事件桥接
│   ├── Framework/
│   │   ├── AB/                 # AssetBundle 管理（ABMgr / ABUpdateMgr / LuaMgr）
│   │   ├── Json/LitJson/       # JSON 库
│   │   ├── Scene/SceneMgr.cs   # 场景管理器
│   │   └── ProtobufTool/       # Protobuf 序列化/反序列化工具
│   └── data/Game.cs            # Protobuf 的 C# 生成代码
├── Scenes/
│   ├── BeginScene.unity        # 大厅场景
│   └── GameScene.unity         # 游戏场景（地形 Batches 3500+ 待优化）
├── Plugins/
│   ├── KCP/                    # KCP C 库各平台编译产物
│   ├── Protobuf/               # Google.Protobuf.dll
│   └── XLua/                   # XLua 插件
├── ArtRes/                     # 美术资源（角色模型 HeroDefault 等）
├── ABRes/                      # 打包后的 AssetBundle
└── StreamingAssets/            # 随包资源
DataBase/                       # 数据库相关（独立于 Unity 项目）
Protobuf/                       # proto 定义 + 生成产物
```

---

## 核心架构

### 帧同步流程

```
客户端                            主机（HostServer）
──────                            ──────────
InputHandler (60fps 采集)         
  ↓                               
PlayerController (输入提交 30fps)
  ↓ NetMgr.Send(PlayerInput)      → TickSyncHandler 收集所有玩家输入
                                     ↓ 收齐或超时 → 组装 InputTick
                                     ↓ 广播 InputTick + 执行确定性物理
TickExecutor.OnApplyPlayerInput   ← NetMgr 接收 InputTick
  ↓ 回调到 Lua
PlayerManager:ApplyFrameInput      每玩家设置 moveDir/isJumping/yaw/...
  ↓
PlayerManager:OnFrameEnd           远程玩家 _ApplyDeterministicMovement
                                       ↓ CharacterController.Move (子步物理)
                                       ↓ 保存 prevPos/targetPos → 插值器驱动
```

### 远程玩家插值系统（60fps 平滑）

```
_ApplyDeterministicMovement (30fps):
  prevPos = 旧 targetPos           ← 链式传递
  物理执行 CharacterController.Move
  targetPos = 结果位置               ← 物理终点
  Transform 回退到 prevPos          ← 渲染滞后 1 tick

_InterpolateRemotePlayers (60fps):
  elapsed += dt
  t = smoothstep(elapsed/interval)  ← t²(3-2t)，起止点导数为 0
  Transform.position = Lerp(prevPos, targetPos, t)
  超时外推（最多 2 tick，衰减速度）
```

### Phase 2 权威位置校正（硬回滚）

- 服务端在每个 `InputTick` 中附带上一 tick 执行后的权威位置（`result_pos_x/y/z`）
- 客户端收到后比较 `serverPos` 与本地 `prevPos`
- 误差 > 1cm：`prevPos = serverPos`，保留本 tick 位移增量叠加到 serverPos 上
- 插值器下一帧从校正后的位置出发，视觉上无跳跃

### 双模式

- **主机模式**：`KcpMgr.ClientConv == 0`，输入通过 `HostServer.SubmitHostInput` 本地提交
- **客户端模式**：输入通过 `NetMgr.Send(PlayerInput)` 发送到主机

### 全局 Update 驱动链

```
Unity Update (C# GameMgr)
  → luaUpdate.Action(dt)             // InitClass.lua 中的 Update()
    → RegisterUpdate 注册的回调        // PlayerController / PlayerManager 插值器
```

---

## 当前开发状态

### 已完成
- [x] KCP 网络框架（握手/收发/心跳/断线重连）
- [x] 房间系统（创建/加入/玩家列表/开始游戏）
- [x] 帧同步（30fps TickSync + TickExecutor + Lua 回调链）
- [x] UI 框架（12 个面板 + UICamera 双层渲染防穿透）
- [x] 角色动画（Animator 驱动 HSpeed/VSpeed/Jump/Roll/Fire/Skill）
- [x] 移动 + 摄像机（WASD + 鼠标 FPS 视角 + CharacterController）
- [x] 跳跃/翻滚
- [x] 远程玩家插值（30fps → 60fps smoothstep）
- [x] Phase 2 服务端权威位置校正
- [x] AssetBundle 资源管理
- [x] **水晶系统**：5 区域独立生成、距离拾取、持有数×6 分、死亡掉落 30% 向上取整、重生
- [x] **阶段系统**：准备→生成→攻击 三轮交替（127s），生成阶段不能打、攻击阶段停生、全程可捡
- [x] Proto 新增：PhaseSwitch(38)、CrystalDrop(39)

### 待完成
- [ ] **攻击/射击系统**：射线检测逻辑（[PlayerController.lua:376](Assets/Lua/Battle/PlayerController.lua#L376) — 阶段开关已加，攻击逻辑待实现）
- [ ] **水晶 Prefab 配置**：需要给 `ArtRes/.../Crystal_01.prefab` 添加 Tag="Crystal" + CrystalComponent 组件，并打包进 AssetBundle
- [ ] **场景生成区域配置**：需要在 GameScene 中放置 5 个 `CrystalSpawnZone` 空节点
- [ ] **画面流畅度**：远程玩家画面仍有卡顿待处理
- [ ] **游戏场景地形优化**：当前 Batches 3500+，DrawCall 偏高
- [ ] ParrelSync 多客户端调试支持（插件已导入）

### 水晶系统规则
| 参数 | 值 |
|---|---|
| 游戏时长 | 127 秒（7s 准备 + 3 轮 20s 生成 + 20s 攻击） |
| 每水晶分数 | 6 分 |
| 生成区域 | 5 个圆形区域（场景 `CrystalSpawnZone` 组件配置） |
| 每区域间隔 | 1.5 秒/颗 |
| 同时在场上限 | **无** |
| 拾取方式 | 距离检测（0.8m），全程可捡 |
| 死亡掉落 | ceil(持有数 × 0.3)，掉在死亡位置附近 |
| 胜负 | 127s 结束时分数最高者获胜，并列则并列获胜 |

---

## 开发约定

### C# ↔ Lua 互调

```lua
-- Lua 调用 C#
CS.UnityEngine.GameObject.Find("...")
CS.HostServer.Instance:SubmitHostInput(...)
CS.Fix64.FromFloat(value).Raw            -- float → Fix64.Raw (long)

-- C# → Lua 回调（通过全局函数/静态字段）
CS.LuaEventBridge.OnGameStart = function(msg) ... end
CS.TickExecutor.OnApplyPlayerInput = function(input) ... end
```

### Protobuf 消息约定

- 所有消息通过 `NetMessage` envelope 的一对多字段 `msg` 包装（见 `game.proto:7`）
- 帧同步输入携带 `sfixed64` 类型的 `camera_yaw` / `charge_time`，C# 侧为 `long`（Fix64.Raw）
- 滚动编码在 `moveDir` 的 bit 4（`GC.MOVE_ROLL = 16`），不修改 proto 定义
- 心跳 2s 间隔，服务器 15s 超时判离线

### 物理参数

| 参数 | 值 |
|---|---|
| 移动速度 | 5 m/s |
| 跳跃初速 | 8 m/s |
| 重力 | 20 m/s² |
| 翻滚速度 | 12 m/s，持续 0.5s |
| 逻辑帧率 | 30 fps |
| 物理子步 | 8（≈120fps 碰撞精度） |
| CharacterController | height=1.8, radius=0.4, stepOffset=0.3 |

### 出生点规则

场景中 `PlayerSpawnPoint/PS1~PS4` 四个子对象：
- 1人：PS1
- 2人：PS1 + **PS3**（拉开距离，公平 1v1）
- 3-4人：按序号分配 PS1/PS2/PS3/PS4

### XLua 注意事项

- `GetComponent` 返回 C# null 时 Lua 侧不是 nil，必须用 `IsNull()` 判断
- `print()` 输出到 Unity Console（通过 XLua 重定向）
- 确定性随机数用 `DeterministicRandom`（Lua 侧），种子由 GameStart 下发

---

## 常用操作

### 启动/调试

1. Unity 打开项目，加载 `BeginScene`
2. 运行 → 大厅 UI → 创建房间 → 加入房间（可用 ParrelSync 开第二个客户端）
3. 房主点击开始游戏 → 切换到 `GameScene` → 30fps 帧同步启动

### Protobuf 更新

```bash
# 修改 Protobuf/proto/game.proto 后
cd Protobuf
./protoc --csharp_out=csharp --proto_path=proto proto/game.proto
# 将生成的 Game.cs 覆盖到 Assets/Scripts/data/ 和 Protobuf/csharp/
```

### AssetBundle 打包

Editor 工具：`Assets/Editor/ABTools.cs` → 打包到 `ABRes/`

---

## 自定义 Skill

在 `.claude/skills/` 下定义了 5 个项目专用 Skill，通过 `/skill-name` 调用：

| Skill | 用途 |
|---|---|
| `/fix-bug` | 修复 Bug——诊断问题时**自动排除 `Assets/Lua/Test/`**，只看生产代码，避免测试用例干扰判断 |
| `/gen-proto` | 修改 `game.proto` 后一键重新生成 C# 代码，并同步到 `Assets/Scripts/data/` 和 `Protobuf/csharp/`，检查 `NetMgr.OnRecvData` 的 switch case 覆盖 |
| `/multi-test` | 使用 ParrelSync 快速启动多个 Unity 实例（房主 + 客户端），并排对比位置同步、画面流畅度、动画一致性 |
| `/net-debug` | 分析 `Logs/` 目录的帧同步日志：提取 Tick 号连续性、位置校正漂移量、网络丢包/超时，输出诊断报告 |
| `/test-sync` | 帧同步确定性物理测试：166+ 条用例覆盖单 tick 移动/多 tick 累积/确定性验证/插值系统/边界条件，单进程内跑不依赖网络 |
