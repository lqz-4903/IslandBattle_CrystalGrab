---
name: gen-proto
description: 修改 game.proto 后一键重新生成 C# 代码并同步到所有目标位置
---

# Protobuf 代码生成

修改 `Protobuf/proto/game.proto` 后运行此 Skill，自动完成 proto 编译、代码生成、多目录同步。

## 执行步骤

### 1. 检查 proto 文件是否有未保存的修改

```bash
git -C <project_root> diff Protobuf/proto/game.proto
```

如果有改动但未提交，先展示 diff 并告知用户。

### 2. 运行 protoc 编译

```bash
cd <project_root>/Protobuf
./protoc --csharp_out=csharp --proto_path=proto proto/game.proto
```

检查执行结果：
- ✓ 成功 → 继续步骤 3
- ✗ 失败 → 输出错误信息，**停止**（常见问题：proto3 语法错误、字段标签冲突、oneof 命名冲突）

### 3. 同步生成文件到两个目标位置

生成产物在 `Protobuf/csharp/Game.cs`，需要同步到：

```
目标 1: Protobuf/csharp/Game.cs       (已有，protoc 直接覆盖)
目标 2: Assets/Scripts/data/Game.cs   (需要手动复制)
```

```bash
cp <project_root>/Protobuf/csharp/Game.cs <project_root>/Assets/Scripts/data/Game.cs
```

### 4. 验证一致性

用 diff 检查两个文件是否完全相同：

```bash
diff <project_root>/Protobuf/csharp/Game.cs <project_root>/Assets/Scripts/data/Game.cs
```

- ✓ 无差异 → 完成
- ✗ 有差异 → 检查是否有手动修改未合入 proto，**警告用户**

### 5. 关键字段检查（防止 proto → C# 映射断链）

如果新增了字段，确认：

- **sfixed64 字段** → C# 侧类型为 `long`（Fix64.Raw），Lua 侧用 `Fix64.new(raw)` 读取
- **oneof 字段** → 确认 `NetMessage.MsgOneofCase` 枚举和 switch case 是否新增了匹配分支
- **repeated 字段** → C# 侧为 `RepeatedField<T>`，Lua 侧用 `.Count` 和 `[index]` 访问

检查 `Assets/Scripts/Net/NetMgr.cs` 的 `OnRecvData` 方法中的 switch case 是否覆盖了新消息类型。

### 6. 输出报告

```
=== Protobuf 代码生成完成 ===
源文件:    Protobuf/proto/game.proto
生成文件:  Protobuf/csharp/Game.cs    (~xxx 行)
同步目标:  Assets/Scripts/data/Game.cs  (✓ 一致)
新增字段:  [列出]（或"无"）
::如果是新增消息类型::
⚠ NetMgr.OnRecvData 的 switch case 需要手动添加新消息类型
```
