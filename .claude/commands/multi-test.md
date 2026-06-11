---
name: multi-test
description: 使用 ParrelSync 快速启动多客户端联机测试
---

# 多客户端联机测试

使用 ParrelSync 插件快速启动多个 Unity 实例，并排对比主机端和客户端表现。

**核心原则：只启动未运行的实例，已运行的窗口不重复启动。**

## 前置条件

- 项目中已安装 ParrelSync 插件（`Assets/Plugins/ParrelSync/`）
- ParrelSync 克隆已创建（`ParrelSync/Clones/` 下至少有一个副本）
- Unity Hub 已安装

## 执行步骤

### 1. 探测已运行的 Unity 实例

**必须先执行**，避免重复启动。通过 Windows 进程名 + 命令行参数识别：

```powershell
# 列出所有 Unity 进程及其项目路径
Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | 
    Select-Object ProcessId, CommandLine | 
    ForEach-Object { $_.CommandLine -match '-projectPath\s+"?([^"\s]+)"?'; 
        Write-Host "PID=$($_.ProcessId)  Project=$($Matches[1])" }
```

解析出：
- 是否有原始项目已运行（`-projectPath` 匹配 `<project_root>`）
- 是否有克隆项目已运行（`-projectPath` 匹配 `<project_root>\ParrelSync\Clones\*`）

### 2. 查找 Unity Editor 可执行文件

从已运行进程的命令行中提取 Unity.exe 路径：

```powershell
Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Select-Object -First 1 -ExpandProperty ExecutablePath
```

或从 `ProjectSettings/ProjectVersion.txt` 读版本号后拼接路径：

```bash
VER=$(cat <project_root>/ProjectSettings/ProjectVersion.txt | head -1)
# 常见路径
echo "C:/Program Files/Unity/Hub/Editor/$VER/Editor/Unity.exe"
echo "D:/Unity/$VER/Editor/Unity.exe"
```

### 3. 确定需要启动的实例

| 角色 | 项目路径 | 说明 |
|---|---|---|
| 房主（Host） | `<project_root>` | 原始项目 |
| 客户端（Client） | `<project_root>/ParrelSync/Clones/<cloneName>` | ParrelSync 克隆 |

对每个角色：
- **已运行** → 跳过启动，告知用户"该实例已在运行中（PID=xxx）"
- **未运行** → 启动新实例

### 4. 启动未运行的 Unity 实例

```bash
# 房主实例（原始项目）
"<UnityEditorPath>" \
    -projectPath "<project_root>" \
    -logFile "<project_root>/Logs/host_$(date +%H%M%S).log" &

# 客户端实例（ParrelSync 克隆，用第一个 clone）
CLONE_PATH=$(ls -d <project_root>/ParrelSync/Clones/*/ 2>/dev/null | head -1)
"<UnityEditorPath>" \
    -projectPath "$CLONE_PATH" \
    -logFile "<project_root>/Logs/client_$(date +%H%M%S).log" &
```

### 5. 启动后状态报告

输出清晰的表格告知用户当前状态：

```
=== Unity 实例状态 ===
房主实例:   [已启动/PID=12345] 或 [已运行/PID=12345 跳过]
客户端实例: [已启动/PID=12346] 或 [已运行/PID=12346 跳过]
日志目录:   <project_root>/Logs/
```

### 6. 操作指引

两个窗口并排后，用户需手动操作：

```
┌─────────────────────────────┐  ┌─────────────────────────────┐
│   窗口 1（房主）              │  │   窗口 2（客户端）            │
│   BeginScene                 │  │   BeginScene                 │
│                              │  │                              │
│  1. 点击"创建房间"            │  │  2. 输入房间号 → "加入房间"    │
│     → 记下房间号              │  │                              │
│                              │  │                              │
│  3. 等待玩家加入              │  │                              │
│  4. 点击"开始游戏"            │  │  自动切换到 GameScene          │
│     → 切换到 GameScene        │  │                              │
└─────────────────────────────┘  └─────────────────────────────┘

※ 如果某个窗口已在运行，直接在对应窗口操作即可。
```

### 7. 对比观察重点

联机测试时重点观察：

- **位置同步**：两个窗口的玩家位置是否一致（0.1m 以内视为正常）
- **画面流畅度**：远程玩家移动是否卡顿（15fps tick → 60fps 插值是否有跳动）
- **动画同步**：跳跃/翻滚/受击动画两端是否一致
- **Console 日志**：是否有异常错误（`Fix64` 溢出、`Controller.Move` 失败、网络超时）

### 8. 日志收集（可选）

```bash
# 测试结束后收集关键日志
LOG_DIR="<project_root>/Logs"
LATEST_HOST=$(ls -t "$LOG_DIR"/host_*.log 2>/dev/null | head -1)
LATEST_CLIENT=$(ls -t "$LOG_DIR"/client_*.log 2>/dev/null | head -1)
grep -E "\[PlayerManager\]|\[NetMgr\]|\[KcpMgr\]|\[TickSync" "$LATEST_HOST" > "$LOG_DIR/summary_host.txt"
grep -E "\[PlayerManager\]|\[NetMgr\]|\[KcpMgr\]|\[TickSync" "$LATEST_CLIENT" > "$LOG_DIR/summary_client.txt"
```
