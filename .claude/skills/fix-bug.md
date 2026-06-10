---
name: fix-bug
description: 修复项目 Bug——诊断问题时自动排除 Assets/Lua/Test/ 测试用例目录，只关注生产代码
---

# Bug 修复

修复 Bug 或调试问题时，严格遵循以下约束。

## 核心约束：禁止读取测试用例

**在诊断和修复 Bug 的整个过程中，禁止读取 `Assets/Lua/Test/` 目录下的任何文件。**

`Assets/Lua/Test/` 是帧同步确定性物理测试用例目录，包含以下内容：
- `TestRunner.lua` / `TestEnv.lua` / `TestFramework.lua` — 测试框架
- `GroupA_Move.lua` ~ `GroupX_RefactorRegression.lua` — 各组测试用例

这些文件是测试代码，不是生产代码。阅读它们会：
- 引入与 Bug 无关的信息噪音
- 误导对生产代码逻辑的判断
- 浪费上下文窗口

### 允许的操作

- `Glob` 列出 `Assets/Lua/Test/` 的文件名（仅确认目录结构，不读内容）
- 搜索时显式排除该目录：`glob` 参数或 `path` 限定在 `Assets/Lua/` 下的非 Test 子目录

### 禁止的操作

- `Read` 任何 `Assets/Lua/Test/` 下的文件
- `Grep` 时包含 `Assets/Lua/Test/` 路径
- 以任何方式将测试用例内容纳入分析

### 正确做法示例

```bash
# ✓ 搜索时限定在生产代码目录
# Grep pattern 时 path 指定为 Assets/Lua/Battle/ 或 Assets/Lua/Core/

# ✓ Glob 确认目录存在（不读内容）
# Glob Assets/Lua/Test/  →  只看文件列表
```

## 执行流程

### 1. 理解 Bug

- 从用户描述中提取：现象、复现步骤、影响范围
- 确定涉及的模块（帧同步/网络/物理/UI/输入）

### 2. 定位相关代码（仅生产代码）

根据 Bug 类型，聚焦以下目录：

| Bug 类型 | 优先查看 |
|---|---|
| 移动/物理/位置漂移 | `Assets/Lua/Battle/PlayerController.lua`, `Assets/Lua/Core/PlayerManager.lua`, `Assets/Scripts/Core/FSLibs/Fix64.cs` |
| 网络/同步/断线 | `Assets/Scripts/Net/NetMgr.cs`, `Assets/Scripts/NetLogic/TickSyncHandler.cs`, `Assets/Scripts/NetLogic/HostServer.cs` |
| UI 问题 | `Assets/Lua/UI/` |
| 动画 | `Assets/Lua/Core/PlayerEntity.lua`（动画参数设置） |
| 帧同步逻辑 | `Assets/Lua/Core/GameConst.lua`, `Assets/Lua/InitClass.lua`, `Assets/Lua/Main.lua` |
| C# 底层 | `Assets/Scripts/GameMgr.cs`, `Assets/Scripts/NetLogic/` |

### 3. 诊断根因

- 阅读相关生产代码，追踪数据流
- 检查最近的 git 变更：`git diff` 或 `git log --oneline -5`
- 查看 Unity Console 日志（如果有）

### 4. 修复

- 最小化改动范围
- 匹配现有代码风格（命名、注释密度、缩进）
- 修改后说明改了什么、为什么这样改

### 5. 验证（可选）

- 如果需要跑测试验证，使用 `/test-sync`
- 如果需要联机验证，使用 `/multi-test`

## 与 `/test-sync` 的关系

- `/fix-bug` — 修改生产代码，不读测试
- `/test-sync` — 运行/编写测试用例，需要读 `Assets/Lua/Test/`
- 修完 Bug 后可用 `/test-sync` 验证是否引入回归
