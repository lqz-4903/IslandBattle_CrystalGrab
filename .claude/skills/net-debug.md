---
name: net-debug
description: 分析 Logs 目录中的帧同步日志，排查联机卡顿、位置漂移、输入丢失等问题
---

# 帧同步网络调试

从 `Logs/` 目录提取日志，整理帧同步关键事件时间线，标出异常点。

## 执行步骤

### 1. 找到最新的日志文件

```bash
ls -lt <project_root>/Logs/*.log 2>/dev/null | head -5
```

如果有 `host.log` 和 `client.log`（multi-test 产物），同时分析两份。

### 2. 提取关键事件

按日志来源分类提取：

#### 帧同步（TickSyncHandler / TickExecutor）

```bash
grep -n -E "TickSync|TickExecutor|\[Tick\]|InputTick|OnApplyPlayerInput|OnAfterTickExecuted" <logfile>
```

关键检查：
- Tick 号是否连续？不连续 = **丢帧**
- 单帧等待时长是否超过 100ms（MaxWaitTime）？ 超过 = **客户端卡顿导致漂移**
- InputTick 是否包含所有活跃玩家的输入？ 缺失 = **客户端输入丢失**

#### 玩家管理器（PlayerManager）

```bash
grep -n -E "\[PlayerManager\]" <logfile>
```

关键检查：
- `硬回滚校正 玩家X 漂移=XXXm`：漂移距离越大说明越严重
  - `< 0.05m`：正常
  - `0.05~0.3m`：轻微，可接受
  - `> 0.3m`：**严重漂移**，需排查网络延迟或物理差异
- `收到权威位置 玩家X pos=(...)`：确认服务端位置是否正确下发
- `帧 X 已执行`：确认 tick 正常推进

#### 网络层（NetMgr / KcpMgr）

```bash
grep -n -E "\[NetMgr\]|\[KcpMgr\]" <logfile>
```

关键检查：
- `ClientConv 为 0`：KCP 握手未完成，**连接问题**
- `SendAsync` 失败 / `TryRecv` 为空持续：**网络断连**
- `KickOff` 消息：**玩家主动/被动离开**

#### 心跳与离线

```bash
grep -n -E "Heartbeat|Offline|心跳" <logfile>
```

### 3. 输出诊断报告

```
=== 帧同步诊断报告 ===
日志文件: [文件名]
时间范围: tick [N] ~ tick [M]

## 帧同步健康度
- 总 tick 数: XXX
- 丢帧: X 次
- 单帧超时 (>100ms): X 次
- 平均帧间隔: XXms (期望 66.7ms @15fps)

## 位置校正统计
- 总校正次数: XXX
- 轻微漂移 (<0.05m): XXX 次
- 中等漂移 (0.05~0.3m): XXX 次
- 严重漂移 (>0.3m): XXX 次 ← [需关注]
- 最大漂移: X.XXXm @ tick X

## 网络状态
- 心跳断连: [有/无]
- KCP 重传: [次数]
- ClientConv 状态: [正常/异常]

## 建议
[根据发现的问题给出针对性建议]
```

### 4. 常见问题对应排查方向

| 现象 | 排查方向 |
|---|---|
| 远程玩家移动卡顿 | `_InterpolateRemotePlayers` 的 `elapsed` 是否正常累加，smoothstep t 值是否平滑 |
| 远程玩家瞬移 | 硬回滚校正触发太频繁，检查 `_serverAuthPos` 到达延迟 |
| 本地玩家抖动 | `CharacterController.Move` 返回值检查，`isGrounded` 波动 |
| 画面一顿一顿 | tick 间隔不稳定，网络延迟抖动，KCP 拥塞控制 |
| 玩家穿墙 | `PHYSICS_SUBSTEPS` 是否足够（当前 8），碰撞体配置 |
| 动画不匹配 | Animator `SetFloat/SetBool` 远程和本地调用路径是否一致 |
