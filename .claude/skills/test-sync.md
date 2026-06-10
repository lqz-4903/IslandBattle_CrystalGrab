---
name: test-sync
description: 帧同步确定性物理测试——单进程内验证移动/插值/校正，不依赖网络，改代码后即时跑
---

# 帧同步确定性物理测试

**一句话**：给定输入 → 跑物理 → 检查输出。单进程内跑，不需要网络、不需要第二个窗口。

---

## 测试框架

### 核心思路

每条测试都是一个纯函数：

```
Test = {
  name,       -- 名称
  setup,      -- 初始: pos/yaw/isGrounded
  inputs,     -- 一或多个 tick 的输入
  assert,     -- 执行 N tick 后检查位置/状态
}
```

### 执行环境

- 不依赖网络、不依赖 HostServer、不依赖 TickSyncHandler
- 创建一个独立的 `GameObject`（带 CharacterController），挂载到隔离的子对象下
- 只调 `PlayerManager:_ApplyDeterministicMovement(player, dt)` 做确定性移动
- 只调 `PlayerManager:_InterpolateRemotePlayers(dt)` 做插值验证
- 可在 **BeginScene 或 GameScene** 中运行，甚至写一个空的 TestScene
- 所有测试结果打印到 Unity Console，`PASS/FAIL` 一目了然

### 关键隔离原则

```
测试用的 PlayerEntity 不经过：
  ✗ InputHandler（不读真实键盘鼠标）
  ✗ NetMgr / KcpMgr（不经过网络）
  ✗ PlayerController（不跑 60fps 预测）
  ✗ HostServer / TickSyncHandler（不跑帧同步循环）

只经过：
  ✓ PlayerManager:_ApplyDeterministicMovement  (15fps 确定性物理)
  ✓ PlayerManager:_InterpolateRemotePlayers   (60fps 插值渲染)
  ✓ PlayerManager:_ApplyServerPositionCorrection (权威校正 — 可选)
```

---

## 测试用例清单

所有用例的坐标约定：`(x, y, z)` = `(右, 上, 前)`。yaw=0 时前方为 +z。

### 容差定义

| 级别 | 值 | 用途 |
|---|---|---|
| `TIGHT` | 0.001m (1mm) | 单 tick 确定性位移 |
| `LOOSE` | 0.01m (1cm) | 多 tick 累积误差 |
| `DIRECTION` | 0.05 | 方向角比较 |

---

### A 组 — 单 tick 基础移动 (13 条)

这些都是核心确定性——**有一条不过就不能联机**。

---

**A1 — 静止无输入不移动**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=0`, `jump=false`
- **执行**: 1 tick × 1/15s
- **断言**:
  - `prevPos == targetPos`（位置完全不变）
  - `transform.position` 不变化

---

**A2 — 向前移动 (yaw=0, W)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=W (1)`, `jump=false`
- **执行**: 1 tick × 1/15s
- **断言**:
  - `targetPos.z - prevPos.z ≈ 0.333m`, 误差 ≤ `TIGHT`
  - `targetPos.x == prevPos.x`（无侧移）
  - `targetPos.y == prevPos.y`（高度不变）

---

**A3 — 向后移动 (yaw=0, S)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=S (2)`
- **断言**: `z 减少 ≈ 0.333m`, `x/y 不变`

---

**A4 — 向左移动 (yaw=0, A)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=A (4)`
- **断言**: `x 减少 ≈ 0.333m`（yaw=0 时 A=左=-x）, `z 不变`

---

**A5 — 向右移动 (yaw=0, D)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=D (8)`
- **断言**: `x 增加 ≈ 0.333m`, `z 不变`

---

**A6 — 斜向移动 W+A (yaw=0)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir = W|A = 5`
- **断言**:
  - `总位移量 ≈ 0.333m`（normalized 后速度不变）
  - `targetPos.x < prevPos.x`（左）
  - `targetPos.z > prevPos.z`（前）
  - 方向角 ≈ -45°（相对前方）

---

**A7 — 斜向移动 S+A (yaw=0)**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir = S|A = 6`
- **断言**:
  - `总位移量 ≈ 0.333m`
  - `x 减少`, `z 减少`

---

**A8 — yaw=90° (朝右) 按 W 前进**

- **初始**: `pos=(0, 0, 0)`, `yaw=90°`, `isGrounded=true`
- **输入**: `moveDir=W (1)`
- **断言**:
  - `targetPos.x 增加 ≈ 0.333m`（朝右走所以 x+）
  - `targetPos.z ≈ 0`（前后方向无位移）

---

**A9 — yaw=180° (朝后) 按 W 前进**

- **初始**: `pos=(0, 0, 0)`, `yaw=180°`, `isGrounded=true`
- **输入**: `moveDir=W (1)`
- **断言**:
  - `targetPos.z 减少 ≈ 0.333m`（朝后走所以 z-）
  - `targetPos.x ≈ 0`

---

**A10 — 翻滚**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir = W|ROLL = 17`（bit0 + bit4）
- **执行**: 1 tick
- **断言**:
  - `位移量 ≈ 12/15 = 0.8m`
  - `位移量 > 0.333m`（翻滚比走路快）
  - `方向正确（z+）`

---

**A11 — 跳跃首帧**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`, `isGrounded=true`
- **输入**: `moveDir=W, jump=true`
- **执行**: 1 tick
- **断言**:
  - `targetPos.y > prevPos.y`（上升了）
  - `水平位移 ≈ 0.333m`（跳跃不影响水平移动）
  - `isGrounded` 变为 `false`（已离地）

---

**A12 — 空中无输入（重力）**

- **初始**: `pos=(0, 2, 0)`, `velocity=(0, 8, 0)`, `isGrounded=false`
- **输入**: `moveDir=0, jump=false`
- **执行**: 1 tick
- **断言**:
  - `y 下降`（重力生效）
  - `xz 不变`

---

**A13 — 空中可移动**

- **初始**: `pos=(0, 2, 0)`, `velocity=(0, 8, 0)`, `isGrounded=false`, `yaw=0°`
- **输入**: `moveDir=W`
- **执行**: 1 tick
- **断言**:
  - `y 下降`（重力仍然生效）
  - `z 增加`（空中可以水平移动）
  - `x 不变`

---

### B 组 — 多 tick 累积一致性 (6 条)

这些验证**多帧累积后确定性不漂移**。

---

**B1 — 连续 30 tick 匀速前进**

- **初始**: `pos=(0, 0, 0)`, `yaw=0°`
- **输入**: 30 个相同 tick（`moveDir=W`）
- **断言**:
  - `总位移 ≈ 30 × 0.333 = 10m`, 误差 ≤ `LOOSE` (1cm)
  - `每 tick 位移一致`，波动 ≤ `TIGHT`

---

**B2 — 走 5 tick → 停 5 tick**

- **输入**: 前 5 tick `W`, 后 5 tick `无输入`
- **断言**:
  - `前 5 tick z 递增`
  - `后 5 tick 位置不变`（松手即停）

---

**B3 — 前进 5 tick → 后退 5 tick（回原点）**

- **初始**: `pos=(0, 0, 0)`
- **输入**: 前 5 tick `W`, 后 5 tick `S`
- **断言**:
  - `最终位置 ≈ 原点`, 误差 ≤ `LOOSE`
  - `每 tick 步长一致`

---

**B4 — 走路 + 跳跃 + 落地**

- **初始**: `pos=(0, 0, 0)`, `isGrounded=true`
- **输入**: tick1~3 `W`, tick3 `jump=true`, 后续 `无输入` 直到落地
- **断言**:
  - `无 NaN, 无穿透地面`
  - `落地后 y ≈ 起始高度`
  - `落地后 isGrounded=true`

---

**B5 — 快速换向（W→S→A→D）**

- **输入**: W×3 → S×3 → A×3 → D×3
- **断言**:
  - `每次换向首帧位移方向正确`
  - `无跳动`（相邻 tick 位移变化 < 0.05m）
  - `无 NaN`

---

**B6 — 翻滚结束后恢复正常速度**

- **输入**: `W+ROLL` × 3 → `W` × 3
- **断言**:
  - `前 3 tick 位移量 ≈ 0.8m/tick`
  - `后 3 tick 位移量 ≈ 0.333m/tick`
  - `无速度残留`

---

### C 组 — 确定性验证 (4 条)

这些是**帧同步的基石**——相同输入必须产生完全相同的结果。

---

**C1 — 相同输入跑两遍，逐 tick 位置完全一致**

- **输入序列**: 随机生成 100 tick 的输入序列
- **方法**: 跑两次，分别记录每 tick 后的 `targetPos`
- **断言**:
  - `两个序列的每 tick 位置完全一致`（float 逐位相等，`==` 比较）
  - 不允许任何偏差，哪怕是 1e-6

---

**C2 — 插入空帧不影响有输入的 tick 结果**

- **方法**: 序列 A = `[W, W, 空, W, W]`, 序列 B = `[W, W, W, W]`
- **断言**:
  - `A 的第 1,2,4,5 tick 结果 == B 的第 1,2,3,4 tick 结果`

---

**C3 — Fix64 往返精度**

- **方法**: `float → Fix64.FromFloat → Fix64.Raw → Fix64.new(Raw) → Fix64.toFloat`
- **测试值**: `0, 1, -1, 5.0, 0.33333, -99.5, 1000.0`
- **断言**:
  - `往返误差 ≤ 0.0001`, 即 for 所有测试值

---

**C4 — Lua/C# Fix64 一致性**

- **方法**: C# `Fix64.FromFloat(x).Raw` vs Lua `Fix64.fromFloat(x).raw`
- **测试值**: 同 C3
- **断言**:
  - `C# Raw == Lua raw`（long 值完全相等）
  - 如果有差异 → 两边的 Fix64 实现不一致，是一切漂移的根因

---

### D 组 — 插值系统 (5 条)

这些验证 **60fps 渲染层**的 smoothstep 插值正确性。

---

**D1 — 插值起点 (elapsed=0)**

- **初始**: `prevPos=(0,0,0)`, `targetPos=(10,0,0)`, `elapsed=0`
- **执行**: `_InterpolateRemotePlayers(dt=1/60)`
- **断言**:
  - `transform.position ≈ prevPos`（在起点，误差 ≤ 0.01）

---

**D2 — 插值中点 (elapsed=1/30, t≈0.5)**

- **初始**: 同上, `elapsed=1/30`（半个 tick 间隔）
- **断言**:
  - `position.x 在 0~10 之间`
  - `不是简单线性`（smoothstep 值 != 0.5 精确中点）

---

**D3 — 插值终点 (elapsed=1/15, t=1.0)**

- **初始**: 同上, `elapsed=1/15`（正好一个 tick）
- **断言**:
  - `position == targetPos`（到达目标）
  - `误差 ≤ 0.001`

---

**D4 — tick 延迟外推 (elapsed=1/15 + 1/30)**

- **初始**: `prevPos=(0,0,0)`, `targetPos=(10,0,0)`, `elapsed = 1/15 + 1/30`, `prevVelocity=(10,0,0)`
- **断言**:
  - `position.x > 10`（外推，超出 targetPos）
  - `position.x < 10 + 10*2/15`（不超过 maxExtrap 上限）

---

**D5 — tick 链式传递 (A→B→C)**

- **方法**: tick1 = `A(0,0,0)→B(10,0,0)`, tick2 = `B(10,0,0)→C(20,0,0)`
- **断言**:
  - `tick2 的 prevPos == B（tick1 的 targetPos）`
  - `tick2 开始时 transform.position == B`
  - `无跳跃`

---

### E 组 — 边界条件 (4 条)

---

**E1 — 全零状态不崩溃**

- **初始**: 所有字段为默认值（0/nil/false）
- **断言**: `不崩溃，不报错，position 不变`

---

**E2 — yaw 超大值不异常**

- **输入**: `yaw = 720°`（两圈）, `moveDir=W`
- **断言**:
  - `sin(720°) ≈ sin(0°) = 0`, `cos(720°) ≈ cos(0°) = 1`
  - `位移方向同 yaw=0°`

---

**E3 — Fix64 极限值不溢出**

- **初始**: `pos=(99999, 99999, 99999)`
- **输入**: `moveDir=W`
- **断言**: `不溢出，位移量正常 ≈ 0.333m`

---

**E4 — 连续跳跃只响应第一次**

- **输入**: `jump=true` × 3 tick（粘滞按键）
- **断言**:
  - `tick1: isGrounded→false, y 增加`
  - `tick2, tick3: 不再次跳跃`
  - `_jumpInitiated 只在 tick1 为 true`

---

### F 组 — 权威位置校正 / 硬回滚 (8 条)

这些测试 Phase 2 的核心逻辑：服务端下发权威位置 → 客户端校正本地插值状态。**这是幻影移动的直接战场**。

---

**F1 — 无漂移不触发校正**

- **初始**: `_interpState.prevPos=(5,0,0)`, `_interpState.targetPos=(5.33,0,0)`, `_serverAuthPos=(5,0,0)`（服务端说 tick N-1 在 5，客户端也在 5）
- **执行**: `_ApplyServerPositionCorrection`
- **断言**:
  - `prevPos 不变`（误差 < 1cm，不触发校正）
  - `targetPos 不变`
  - `_serverAuthPos 被清为 nil`（已消费）

---

**F2 — 小漂移校正（< 5cm）**

- **初始**: `prevPos=(5, 0, 0)`, `targetPos=(5.33, 0, 0)`, `serverAuthPos=(4.97, 0, 0)`（漂移 3cm）
- **断言**:
  - `newPrevPos == (4.97, 0, 0)`（硬回滚到服务器位置）
  - `newTargetPos == (4.97 + 0.33, 0, 0) = (5.30, 0, 0)`（保留本 tick 位移增量）
  - `prevPos→targetPos 位移量不变`（0.33m）

---

**F3 — 大漂移校正（> 0.3m，严重）**

- **初始**: `prevPos=(5, 0, 0)`, `targetPos=(5.33, 0, 0)`, `serverAuthPos=(4.5, 0, 0)`（漂移 0.5m）
- **断言**:
  - `newPrevPos == (4.5, 0, 0)`
  - `newTargetPos == (4.83, 0, 0)`
  - `elapsed 重置为 0`（重新开始插值周期，视觉无跳跃）
  - `日志输出包含"硬回滚校正"`（>0.3m 需打印）

---

**F4 — XZ 平面漂移同时发生**

- **初始**: `prevPos=(5, 0, 5)`, `targetPos=(5.33, 0, 5.33)`, `serverAuthPos=(4.9, 0, 4.9)`
- **断言**:
  - `newPrevPos == (4.9, 0, 4.9)`
  - `位移增量保留：(0.33, 0, 0.33)`
  - `newTargetPos == (5.23, 0, 5.23)`

---

**F5 — Y 轴漂移不参与校正（高度由物理自身保证）**

- **初始**: `prevPos=(5, 1.5, 5)`, `targetPos=(5.33, 1.5, 5.33)`, `serverAuthPos=(5, 0.8, 5)`（服务端 Y 差 0.7m）
- **断言**:
  - `newPrevPos == (5, 0.8, 5)`（Y 也被校正——服务端权威包括 Y）
  - 或者如果设计为 Y 不校正：`prevPos.y 不变`
  - **关键**：需先明确设计决策——Y 轴是否参与校正

---

**F6 — 校正数据只消费一次**

- **初始**: `_serverAuthPos` 已设置
- **方法**: 连续调两次 `_ApplyServerPositionCorrection`
- **断言**:
  - `第一次：执行校正，_serverAuthPos 变为 nil`
  - `第二次：_serverAuthPos 为 nil，直接跳过`

---

**F7 — 无插值状态时校正不崩溃**

- **初始**: `_interpState = nil`, `_serverAuthPos=(5,0,0)`
- **断言**: `不崩溃，不报错`，跳过校正

---

**F8 — 本地玩家不触发校正**

- **初始**: `player.playerId == localPlayerId`, `_serverAuthPos` 有值
- **断言**: `_serverAuthPos 被忽略`（ApplyFrameInput 中不设置远程玩家的 auth pos）
- 验证：本地玩家调用 `_ApplyServerPositionCorrection` 时 `goto continue_correct` 跳过

---

### G 组 — 位置捕获（主机端 _CaptureAuthPositions）(5 条)

这些验证主机端捕获物理结果并提交给 TickSyncHandler 的流程。

---

**G1 — 主机端远程玩家从 targetPos 取值**

- **场景**: 主机上有两个玩家（本地 + 远程），远程玩家已跑完 `_ApplyDeterministicMovement`
- **初始**: 远程玩家 `_interpState.targetPos = (10, 0, 10)`, `transform.position = (9.67, 0, 9.67)`（回退到了 prevPos）
- **执行**: `_CaptureAuthPositions`
- **断言**:
  - `捕获的远程玩家位置 = targetPos (10,0,10)`，**不是** transform.position
  - `Fix64.Raw 值正确转换`

---

**G2 — 主机端本地玩家从 transform.position 取值**

- **场景**: 主机本地玩家由 PlayerController 60fps 驱动，不走 _interpState
- **断言**:
  - `捕获的本地玩家位置 = transform.position`（当前渲染位置）
  - `不访问 _interpState（为空或忽略）`

---

**G3 — 捕获值 Fix64 往返一致**

- **初始**: 玩家在 `pos=(12.345, 2.0, -8.901)`
- **执行**: float → `Fix64.FromFloat` → `.Raw` (long)
- **断言**:
  - `Fix64.new(Raw).toFloat ≈ 原始 float`，误差 ≤ 0.0001
  - 验证 `FromFloat(12.345).Raw` 和 `FromFloat(-8.901).Raw` 正确

---

**G4 — 所有活跃玩家都被捕获**

- **方法**: 创建 4 个 PlayerEntity，只设 2 个 `isAlive=true`
- **断言**: `SubmitAuthPosition 只被调用 2 次`，对应 isAlive 的玩家

---

**G5 — 无 Transform 时降级不崩溃**

- **初始**: 玩家 `transform = nil` 或 `IsNull(transform) = true`
- **断言**: `跳过该玩家，不崩溃，不影响其他玩家捕获`

---

### H 组 — 插值状态生命周期 (6 条)

这些验证 `_interpState` 的创建、继承、重置在各种场景下正确。

---

**H1 — 首次 tick 自动初始化**

- **初始**: `_interpState = nil`，调用 `_ApplyDeterministicMovement`
- **断言**:
  - `_interpState 被创建`
  - `prevPos = transform.position`（首次用当前 Transform 作起点）
  - `prevYaw = 当前 yaw`
  - `hasTarget = true`

---

**H2 — 重生后插值状态重置**

- **方法**: 玩家死亡 → Respawn 到新位置
- **断言**:
  - `_interpState = nil`（被 `OnServerPlayerRespawn` 清除）
  - `_serverAuthPos = nil`
  - `下一个 tick 自动重新初始化（H1）`

---

**H3 — 连续 tick prevPos 链式传递**

- **输入**: tick1 移动, tick2 移动, tick3 移动
- **断言**:
  - `tick(n).prevPos == tick(n-1).targetPos`（精确相等，float ==）
  - 链不断裂，无回跳

---

**H4 — 中间断 tick 后重新接续**

- **方法**: tick1→tick2（正常）→ tick3 丢失（不调 `_ApplyDeterministicMovement`）→ tick4 正常
- **初始**: tick2 后 `targetPos=B`, tick4 时 `_interpState` 仍保留 tick2 的 targetPos
- **断言**:
  - `tick4.prevPos = tick2.targetPos（B）`（链没断，跳过丢失的 tick3）
  - `tick4.targetPos 从 B 继续前进`

---

**H5 — 插值状态在玩家离线/移除时清理**

- **方法**: 调 `PlayerManager:RemovePlayer(playerId)`
- **断言**:
  - `player._interpState 随 player:Destroy() 释放`
  - `GameObject 被 Destroy`
  - `players[playerId] = nil`

---

**H6 — transform 突然变为 null（GameObject 被意外销毁）**

- **初始**: `_interpState` 有效, `transform` 正常
- **方法**: 外部 `GameObject.Destroy` 后 tick 到达
- **断言**:
  - `不崩溃，_InterpolateRemotePlayers 中检测 IsNull(transform)，跳过`
  - `Console 有 error 日志`

---

### I 组 — 双玩家位置一致性（同主机侧模拟）(4 条)

这些是**帧同步正确性的终极检验**——主机端和客户端用同一套确定性物理，结果必须一致。

---

**I1 — 两个"远程"玩家同输入→同位置**

- **方法**: 在同一进程中创建 2 个 PlayerEntity（playerId=2 和 playerId=3），都标记为 `isLocal=false`
- **输入**: 两个玩家每 tick 收到**完全相同的输入**（moveDir=W, yaw=0°, jump=false）
- **执行**: 各跑 60 tick 的 `_ApplyDeterministicMovement`
- **断言**:
  - `每 tick 后 targetPos 逐帧相等`（float ==）
  - `60 tick 后两者位置完全一致`

---

**I2 — 两个玩家不同输入→不同位置（独立性）**

- **输入**: 玩家 A: `W`×30, 玩家 B: `S`×30
- **断言**:
  - `A.targetPos.z > 0`（往前走）
  - `B.targetPos.z < 0`（往后走）
  - `两者的物理模拟互不干扰`

---

**I3 — 主机本地玩家 vs 客户端远程玩家（模拟对比）**

- **方法 A（主机侧模拟）**: 用 `_ApplyDeterministicMovement` 跑一个玩家 60 tick, 记录所有 `targetPos`
- **方法 B（客户端侧模拟）**: 用完全相同的初始条件和输入序列跑 `_ApplyDeterministicMovement`
- **断言**:
  - `A 序列和 B 序列逐 tick 完全相等`
  - 这就是帧同步的数学保证——如果有差异，物理或 Fix64 实现不一致

---

**I4 — 两玩家起始位置不同→同输入→同位移量**

- **初始**: `A.pos=(0,0,0)`, `B.pos=(100,0,100)`
- **输入**: 两者相同：`W`×10 tick
- **断言**:
  - `A.targetPos.z = A.prevPos.z + 0.333*10`
  - `B.targetPos.z = B.prevPos.z + 0.333*10`
  - `位移量完全相等`（不受初始位置影响）

---

### J 组 — 状态转换位置处理 (5 条)

---

**J1 — 死亡时 isAlive=false 后不再执行物理**

- **方法**: 玩家走路中 `isAlive = false`
- **断言**:
  - `_ApplyDeterministicMovement 检查 !player.isAlive → 跳过`
  - `位置停留在死亡瞬间`
  - `_InterpolateRemotePlayers 同样跳过`

---

**J2 — 重生后位置 warp 到出生点**

- **初始**: 玩家死亡，`prevPos=(50, -999, 20)`（坠崖）
- **输入**: `OnServerPlayerRespawn(playerId, 0, 2, 10)` → `pos=(0,2,10)`
- **断言**:
  - `player.position = (0, 2, 10)`（Fix64）
  - `transform.position = (0, 2, 10)`（Unity 同步）
  - `_interpState = nil`（清除旧插值状态）
  - `isAlive = true`

---

**J3 — 重生后第一 tick 正常移动**

- **前置**: 完成 J2 重生
- **输入**: 重生后第一个 tick `moveDir=W, yaw=0°`
- **断言**:
  - `prevPos = 重生位置`（新插值起点）
  - `targetPos = 重生位置 + (0,0,0.333)`（正常移动）
  - `与普通移动完全一致`

---

**J4 — 坠落位置校正**

- **初始**: `prevPos=(10, 5, 10)`, 连续多 tick 下落
- **服务端事件**: `OnServerPlayerFall(playerId, droppedCount)`
- **断言**:
  - `HP 扣减正确`
  - `如果 isAlive=false → 物理停止（J1）`
  - `如果 isAlive=true → 位置不变，等待重生事件`

---

**J5 — 受到攻击时位置不受影响**

- **场景**: 玩家在移动中被攻击
- **事件**: `OnServerPlayerHit(attackerId, victimId, ...)`
- **断言**:
  - `HP 扣减`
  - `位置不变（受击不改变位置，只扣 HP/丢水晶）`
  - `动画参数改变但物理状态不变`

---

### K 组 — 主机/客户端位置数据流 (4 条)

这些验证位置数据在高层组件之间传递的完整性。

---

**K1 — ApplyFrameInput 正确提取权威位置**

- **输入**: `PlayerInput` 含 `ResultPosX/Y/Z`（Fix64.Raw 值）
- **执行**: `PlayerManager:ApplyFrameInput(input)`
- **断言**:
  - `远程玩家 _serverAuthPos 被设置`
  - `_serverAuthPos.x/y/z 正确转换 Fix64.new(raw)→Fix64`
  - `本地玩家 _serverAuthPos 不受影响`

---

**K2 — OnFrameEnd 中主机端执行顺序正确**

- **方法**: 验证 `OnFrameEnd(tick)` 中调用的顺序
- **断言顺序**:
  1. `_ApplyDeterministicMovement`（先算物理）
  2. `_CaptureAuthPositions`（再捕获结果）
  3. `_ApplyServerPositionCorrection`（主机端跳过此步）
  - `顺序不能乱——先捕获再校正会拿到错误的位置`

---

**K3 — OnFrameEnd 中客户端执行顺序正确**

- **方法**: 验证客户端 `OnFrameEnd(tick)` 中调用的顺序
- **断言顺序**:
  1. `_ApplyDeterministicMovement`（先算物理）
  2. `_CaptureAuthPositions`（客户端跳过此步）
  3. `_ApplyServerPositionCorrection`（再用服务器位置校正）
  - `先算物理再校正——这是硬回滚的设计`

---

**K4 — 每帧 interpState 不被意外覆盖**

- **方法**: 在 `_InterpolateRemotePlayers` 运行期间，检查是否有代码在修改 `_interpState` 的字段
- **断言**:
  - `只有 _ApplyDeterministicMovement 能修改 prevPos/targetPos/elapsed`
  - `只有 _ApplyServerPositionCorrection 能修改 prevPos/targetPos（校正时）`
  - `_InterpolateRemotePlayers 只读不写（除了 displayHSpeed/VSpeed）`

---

### L 组 — Fix64 精确运算验证 (8 条)

这些验证定点数运算本身不引入误差，是所有确定性计算的基石。

---

**L1 — 加减法精度无损**

- **方法**: `a + b - b == a`（任意 a, b）
- **测试值**: `(1, 0.5), (100, 0.001), (-50, 33.333), (0, 999)`
- **断言**: `(a+b)-b == a`（raw 值完全相等）, 误差 = 0

---

**L2 — 乘法结合律**

- **方法**: `(a * b) * c == a * (b * c)` 在 Fix64 精度内
- **测试值**: `(2, 3, 4), (0.1, 0.2, 0.5), (10, 0.333, 3)`
- **断言**: `结合律误差 ≤ 1 raw unit`（相当于 1/2^32 ≈ 2.3e-10）

---

**L3 — 除法逆运算**

- **方法**: `(a / b) * b ≈ a`
- **测试值**: `(10, 3), (1, 7), (100, 0.5), (-50, 8)`
- **断言**: `误差 ≤ LOOSE`（1cm 等效精度）

---

**L4 — sqrt 自验证**

- **方法**: `sqrt(x) * sqrt(x) ≈ x`
- **测试值**: `1, 2, 4, 100, 0.25, 10000`
- **断言**: `|sqrt(x)² - x| ≤ TIGHT * x`

---

**L5 — lerp 端点**

- **方法**: `lerp(A, B, 0) == A`, `lerp(A, B, 1) == B`
- **断言**: `精确相等`

---

**L6 — lerp 中点**

- **方法**: `lerp(A, B, 0.5)`
- **断言**: `= (A+B)/2`（线性插值中点）, 误差 ≤ TIGHT

---

**L7 — clamp 边界**

- **测试值**: `clamp(-5, 0, 10) = 0`, `clamp(15, 0, 10) = 10`, `clamp(5, 0, 10) = 5`
- **断言**: `精确相等`

---

**L8 — 方向向量归一化不变长度**

- **方法**: `Vec3(3, 0, 4).normalized.length ≈ 1`
- **断言**: `|normalized.length - 1| ≤ TIGHT`

---

### M 组 — 输入编码/解码 (6 条)

验证 moveDir 位掩码、按键粘滞、蓄力计时等输入处理正确性。

---

**M1 — 单键编码对应位**

- **方法**: 分别按 W/A/S/D 各 1 tick
- **断言**: `moveDir 位 0/1/2/3 各自独立`, 无交叉污染

---

**M2 — 组合键编码不冲突**

- **输入**: W+A（位 0+2=5）, S+D（位 1+3=10）, W+A+S+D（位 0+1+2+3=15）
- **断言**: `moveDir 值正确`, `低 4 位 = 方向组合`

---

**M3 — Roll 标志位不干扰方向**

- **输入**: `W+ROLL = 17`, `A+ROLL = 20`
- **断言**: `低 4 位方向不变`, `bit4 为 1`

---

**M4 — 跳跃粘滞**

- **输入**: `GetButtonDown` 只在一帧返回 true
- **模拟**: jumpPressed 拉高后连续 3 次 `GetTickInput`
- **断言**: `第一次 jump=true`, `后两次 jump=false`（只发一次）

---

**M5 — 技能粘滞（同 M4）**

- **断言**: `skill 只在释放的 tick 为 true`

---

**M6 — 蓄力计时归零**

- **模拟**: 按住攻击 2s → 松开 → GetTickInput
- **断言**: `chargeTime>0 只在按住期间`, `松开后归零`

---

### N 组 — 物理边界与碰撞 (6 条)

---

**N1 — 贴墙移动不被卡死**

- **初始**: 玩家紧贴墙壁, `moveDir=W`（沿墙方向）
- **断言**: `沿墙方向仍有位移`, `不是 0`

---

**N2 — 正面撞墙位移为零**

- **初始**: 玩家正面紧贴墙壁, `moveDir=W`（撞墙）
- **断言**: `xz 位移≈0`, `不穿墙`

---

**N3 — 斜坡上下不卡**

- **初始**: 玩家在斜坡上（slope < 45°）
- **断言**: `isGrounded = true`, `上下坡移动正常`

---

**N4 — 小台阶自动翻越（stepOffset=0.3）**

- **初始**: 玩家前方有 ≤0.3m 台阶, `moveDir=W`
- **断言**: `能够越过台阶`, `不被卡住`

---

**N5 — 高速移动不穿透薄墙（子步 8 保护）**

- **输入**: 翻滚速度 12m/s × 1/15s = 0.8m
- **断言**: `不穿透 < 0.8m 的薄墙`（子步拆分有效）

---

**N6 — 空中碰墙停止但不穿墙**

- **初始**: 空中 + 水平速度, 前方有墙
- **断言**: `水平位移被墙挡住`, `y 继续下落（重力）`

---

### O 组 — 确定性随机数 (4 条)

---

**O1 — 同种子→同序列**

- **方法**: 两个 `DeterministicRandom` 实例，相同种子
- **断言**: `前 100 次调用结果逐位相等`

---

**O2 — 不同种子→不同序列**

- **断言**: `seed=42 的序列 ≠ seed=99 的序列`

---

**O3 — 范围随机（整数）**

- **方法**: `RandomInt(min, max)` × 1000 次
- **断言**: `所有结果在 [min, max] 内`, `覆盖边界值（至少出现一次 min 和 max）`

---

**O4 — 范围随机（Fix64）**

- **方法**: `RandomFix64(min, max)` × 1000 次
- **断言**: `所有结果在 [min, max] 内`

---

### P 组 — 帧同步时序边界 (6 条)

---

**P1 — 单帧 inputTick 包含所有玩家**

- **方法**: 3 个玩家都提交了输入
- **断言**: `inputTick.inputs.Count == 3`

---

**P2 — 玩家超时未提交→空输入**

- **方法**: 1 个玩家不提交输入, `MaxWaitTime=0.1s` 超时
- **断言**: `该玩家用 moveDir=0 填充`, `不影响其他玩家`

---

**P3 — tick 号单调递增**

- **方法**: 跑 100 帧
- **断言**: `tick 号从 0 连续递增到 99`, `无跳跃`

---

**P4 — 帧间隔稳定（±容忍）**

- **方法**: 测量连续 tick 之间的实际时间差
- **断言**: `每帧间隔在 1/15 ± 20%`（0.053~0.080s）

---

**P5 — 客户端追赶帧快速消费**

- **方法**: 一次性收到 10 个 CatchUpTicks
- **断言**: `客户端在 < 1s 内消费完所有追赶帧`

---

**P6 — 帧历史溢出不崩溃**

- **方法**: 连续产生超过 `MaxTickHistory`(1500) 帧
- **断言**: `旧帧被淘汰`, `不崩溃`

---

### Q 组 — 多玩家复杂场景 (6 条)

---

**Q1 — 3 玩家全部同一方向**

- **初始**: PS1/PS2/PS3 出生
- **输入**: 3 人全部 `moveDir=W` × 30 tick
- **断言**: `3 人位移量一致`, `互不碰撞穿透`

---

**Q2 — 4 玩家对角移动**

- **初始**: PS1~PS4 四个角
- **输入**: PS1→中心, PS2→中心, PS3→中心, PS4→中心
- **断言**: `4 人汇聚到中心附近`, `没有重叠穿透`

---

**Q3 — 玩家碰撞不穿透**

- **方法**: 两个玩家相向而行
- **断言**: `CharacterController 互相碰撞`, `位置不重叠`

---

**Q4 — 玩家出生点分配正确**

- **方法**: 2 人时验证 PS3 规则
- **断言**: `2 人: player1=PS1, player2=PS3`

---

**Q5 — 新玩家中途加入**

- **断言**: `已有玩家的状态不变`, `新玩家正确初始化`

---

**Q6 — 玩家断线后移除不影响其他玩家**

- **方法**: 移除 player2，剩余 player1/3/4 继续
- **断言**: `player1/3/4 的 tick 正常推进`

---

### S 组 — 统一物理路径基础测试 (12 条)

验证 _ApplyDeterministicMovement 对所有玩家（本地+远程）都正确工作。

---

**S1 — 本地玩家单 tick W 前进**

- **初始**: `isLocal=true`, `pos=(0,0.1,0)`, `yaw=0°`
- **输入**: `moveDir=W`, 执行 `_ApplyDeterministicMovement`
- **断言**: `targetPos.z > prevPos.z`, `x 不变`

**S2 — 本地玩家静止不动**

- **输入**: `moveDir=NONE`
- **断言**: `targetPos.xz ≈ prevPos.xz`（静止不移）

**S3 — 本地玩家多 tick 累积位移**

- **输入**: `moveDir=W` × 30 tick
- **断言**: `30tick 位移 ≈ 10m`（30 * 1/15 * 5 = 10m）

**S4 — 本地玩家跳跃**

- **输入**: `jump=true`, `isGrounded=true`
- **断言**: `targetPos.y > 0.09`（升高）, `isGrounded=false`

**S5 — 本地玩家翻滚**

- **输入**: `moveDir = W|ROLL (17)`
- **断言**: `单 tick 位移 ≈ 0.8m`（12/15）

**S6 — 本地玩家斜向移动**

- **输入**: `moveDir = W|D`
- **断言**: `x>0, z>0`（斜向移动）

**S7 — 本地玩家左移 / S8 — 后退**

- 验证 moveDir=LEFT → x<0, moveDir=BACKWARD → z<0

**S9 — 远程玩家前进（对照组）**

- 同上 S1 但 isLocal=false

**S10 — 本地+远程同输入→同位置（★核心）**

- 两个玩家（isLocal=true + false），30 tick 全同输入
- **断言**: `逐 tick targetPos 完全一致`

**S11 — 本地玩家插值状态初始化**

- **断言**: `_InitInterpState` 创建状态, `hasTarget 初始 false`, `首 tick 后 hasTarget=true`

**S12 — 本地玩家 prevPos→targetPos 链式传递**

- 连续 2 tick
- **断言**: `tick1.targetPos == tick2.prevPos`（链不断）

---

### T 组 — 权威位置捕获一致性 (10 条)

验证 _CaptureAuthPositions 统一从 interpState.targetPos 读取所有玩家位置。

---

**T1 — 远程玩家从 targetPos 捕获**

- **断言**: `_interpState.targetPos` 有效, `Fix64.Raw 转换正确`

**T2 — 本地玩家 targetPos 和 transform 一致性**

- **断言**: `targetPos 存在且有效`, `transform 已被回退到 prevPos`（正常行为）

**T3 — Fix64.Raw 往返不丢失精度**

- **方法**: `targetPos → FromFloat → Raw → Fix64.new → toFloat`
- **断言**: `往返误差 < 0.001`

**T4 — 无 interpState 时降级不崩溃**

- **方法**: `_interpState = nil`
- **断言**: `不走 targetPos 分支`, `降级到 transform.position`

**T5 — 所有玩家都被捕获（含本地）**

- **断言**: `远程 targetPos 存在`, `本地 targetPos 存在`

**T6 — 捕获值来源于确定性物理终点**

- **断言**: `targetPos - prevPos 位移 ≈ 0.33m`（单 tick）

**T7 — _CaptureAuthPositions 方法可安全调用**

- **断言**: `pcall 不崩溃`

**T8 — transform 为 nil 时安全处理**

**T9 — Y 轴（高度）正确传递**

- **初始**: `pos=(0,5,0)`（空中）
- **断言**: `因重力 Y 下降`, `Y 轴 Raw 往返不丢`

**T10 — 同 tick 内连续捕获两次 targetPos 一致**

- **断言**: `两次读取 targetPos 值完全相同`

---

### U 组 — 帧执行与插值顺序 (10 条)

验证 OnFrameEnd 执行顺序和插值系统对统一路径的适配。

---

**U1 — _ApplyDeterministicMovement 对本地玩家生效**

- **断言**: `prevPos/targetPos 均非 nil`, `移动后位置前进`

**U2 — 连续 tick 位置单调递增**

- 5 tick 后位置单调递增

**U3 — ApplyInput 在 Move 之前被调用**

- 有输入位移 > 无输入位移

**U4 — 插值器跳过本地玩家**

- `_InterpolateRemotePlayers` 检查 playerId ≠ localPlayerId
- **断言**: `本地玩家不被插值器修改`

**U5 — 多个远程玩家各自独立插值**

**U6 — 插值状态在 tick 间正确维护**

- 10 tick 后 `hasTarget=true`, `elapsed=0`

**U7 — OnFrameEnd 对所有玩家执行（★核心）**

- 3 玩家（1 本地 + 2 远程）全部执行 `_ApplyDeterministicMovement`
- **断言**: `全部有 targetPos`

**U8 — 重构后不再跳过本地玩家**

- 旧逻辑跳过本地玩家，重构后应全部执行

**U9 — _CaptureAuthPositions 在 Move 之后执行**

- **断言**: `Move 后 targetPos 更新`

**U10 — tick 间无输入时移动停止**

- **断言**: `无输入 tick 后 xz 不变`

---

### V 组 — 双路径一致性验证 (10 条)

对比旧 60fps 路径和新统一 15fps 路径，验证差异并确认统一后的正确性。

---

**V1 — 连续确定性移动位置单调递增**

**V2 — 不同起点同方向位移量一致**

**V3 — 本地/远程在同起点同路径→位置一致（★核心）**

- 15 tick 本地+远程全同输入
- **断言**: `逐 tick targetPos 完全一致`

**V4 — 旧 60fps 路径与新 15fps 路径差异量化**

- 60fps 单帧 ≈ 5/60 = 0.083m vs 15fps 单 tick ≈ 5/15 = 0.333m
- 记录差异，不阻塞

**V5 — 翻滚状态在确定性路径中正确**

**V6 — 空中移动路径一致**

- 本地+远程空中向前，Y 接近

**V7 — 方向切换后位置一致**

- 前进 10 + 后退 10 → 原点附近

**V8 — 跳跃后着地位置一致**

**V9 — Fix64 速度存储一致**

**V10 — 插值状态坐标轴分量符号一致**

---

### W 组 — 输入与移动分离测试 (9 条)

验证 PlayerController 只采集/提交输入，不再直接执行 controller:Move（主机端）。

---

**W1 — PlayerController 和 _SubmitInput 存在**

**W2 — moveDir 位编码常量正确**

**W3 — ApplyTickInput 正确设置 PlayerEntity 字段**

**W4 — moveDir 正确驱动移动方向**

**W5 — 输入可重置（jump 二次为 false）**

**W6 — 翻滚 bit4 与方向低 4 位不冲突**

**W7 — CameraYaw=90° 时 W→x 增加**

**W8 — CameraYaw=180° 时 W→z 负方向**

**W9 — 输入采集与移动执行可独立调用**

- ApplyTickInput 后不立即 Exec，验证分离

---

### X 组 — 重构回归验证 (10 条)

验证重构后所有非移动关键行为仍然正确。

---

**X1 — PlayerManager 单例正常**

**X2 — 玩家创建/销毁/查询正常**

**X3 — 本地/远程标志正确**

**X4 — GetLocalPlayer/GetPlayer 查询正确**

**X5 — 死亡/重生生命周期**

**X6 — 多玩家共存不互相干扰**

**X7 — 原点创建位置正确**

**X8 — CharacterController 参数（height/radius/stepOffset）**

**X9 — 移除玩家后不再存在于查询**

**X10 — Phase 2 校正代码可安全废弃**

---

### R 组 — Fix64 网络序列化 (5 条)

---

**R1 — sfixed64 → Fix64.new(raw) 往返**

- **方法**: `Fix64 value → .raw (long) → Fix64.new(raw) → .raw`
- **测试值**: `0, PI, -100.5, 0.33333, Fix64.MaxValue/2`
- **断言**: `往返 raw 值完全相等`

---

**R2 — PlayerInput 中 CameraYaw 序列化**

- **方法**: Lua `Fix64.fromFloat(1.57)` → `CS.Fix64.FromDouble(1.57)` → 比较 raw
- **断言**: `两侧 raw 值一致`

---

**R3 — ResultPosX/Y/Z 通过网络不丢失精度**

- **方法**: `fix64 → raw → pb sfixed64 → raw → fix64`
- **断言**: `最终 Fix64.toFloat 与原始误差 ≤ 0.0001`

---

**R4 — chargeTime 蓄力时间 Fix64 精度**

- **断言**: `chargeTime 0~5 秒范围内的 Fix64 表示精确度 ≥ 1/2^32`

---

**R5 — 大量 Fix64 值 Protobuf 打包不出错**

- **方法**: 100 个随机 Fix64 值编入 PlayerInput 消息
- **断言**: `序列化/反序列化不崩溃`, `所有值恢复正确`

---

## 汇总

| 组 | 条数 | 重点 |
|---|---|---|
| A — 单 tick 基础移动 | 13 | 每个操作的正确性 |
| B — 多 tick 累积 | 6 | 长时间不漂移 |
| C — 确定性验证 | 4 | 相同输入=相同结果 |
| D — 插值系统 | 5 | 60fps 渲染平滑 |
| E — 边界条件 | 4 | 不崩溃 |
| F — 权威位置校正 | 8 | 硬回滚逻辑——幻影移动的直接战场 |
| G — 位置捕获 | 5 | 主机端取数正确性 |
| H — 插值状态生命周期 | 6 | 创建/继承/重置/销毁 |
| I — 双玩家一致性 | 4 | 帧同步的终极数学保证 |
| J — 状态转换 | 5 | 死亡/重生/坠落/受击 |
| K — 数据流顺序 | 4 | OnFrameEnd 调用链正确 |
| **L — Fix64 精确运算** | **8** | **定点数数学基础验证** |
| **M — 输入编码/解码** | **6** | **moveDir/WASD/粘滞按键** |
| **N — 物理边界碰撞** | **6** | **穿墙/斜坡/台阶/高速** |
| **O — 确定性随机数** | **4** | **同种子→同序列** |
| **P — 帧同步时序** | **6** | **tick连续性/超时/追赶** |
| **Q — 多玩家场景** | **6** | **3-4人/碰撞/中途加入** |
| **R — Fix64 序列化** | **5** | **网络传输精度保障** |
| **S — 统一物理路径** | **12** | **本地玩家走确定性移动（★重构核心）** |
| **T — 权威位置捕获** | **10** | **统一从targetPos捕获所有玩家** |
| **U — 帧执行与插值** | **10** | **OnFrameEnd顺序+插值器适配** |
| **V — 双路径一致性** | **10** | **本地/远程同输入对比验证** |
| **W — 输入与移动分离** | **9** | **PlayerController只采集不执行** |
| **X — 重构回归** | **10** | **改代码后不能坏的一切** |
| **总计** | **166+** | |

---

## 使用方式

### 通过 `/test-sync` 调用时

1. 确认测试文件是否存在：检查 `Assets/Lua/Test/TestRunner.lua` 和 `Assets/Lua/Test/` 下的测试用例
2. 如果测试文件不存在，根据上面的用例清单创建测试代码
3. 在 Unity Editor Console 中查看测试结果 — `PASS` 绿色 / `FAIL` 红色
4. 如果有 FAIL，输出失败用例的名称、预期值、实际值
5. 标记哪些失败是**阻塞性**的（A 组和 C 组）vs **可延后**的（D 组边界场景）

### 改代码前的强制流程

```
修改任何 PlayerManager/PlayerController 代码前：
  1. /test-sync → 记录当前通过率
  2. 改代码
  3. /test-sync → 对比通过率
  4. 全部 PASS 后才联机验证
```

### 测试覆盖的代码路径

测试直接覆盖的函数：
- `PlayerManager._ApplyDeterministicMovement`  ← A/B/C/E/H/I/S/U/V/W 组
- `PlayerManager._InterpolateRemotePlayers`   ← D 组
- `PlayerManager._ApplyServerPositionCorrection` ← F/X 组
- `PlayerManager._CaptureAuthPositions`       ← G/T 组
- `PlayerManager.OnFrameEnd`                  ← K/U 组
- `PlayerManager.ApplyFrameInput`             ← F/K 组
- `PlayerManager.OnServerPlayerRespawn`       ← J 组
- `PlayerController._ApplyLocalMovement`      ← V/W 组（重构影响范围）
- `PlayerController._SubmitInput`             ← W 组
- `Fix64 / Fix64Vector3`（Lua 侧）            ← C3/C4/L 组

测试**不**覆盖（这些属于集成测试，需要联机环境）：
- KCP 收发 / 心跳 / 超时
- Protobuf 序列化 / 反序列化
- 房间创建/加入/开始
- 游戏事件生成（水晶/受击/坠落——但位置响应已覆盖于 J 组）
