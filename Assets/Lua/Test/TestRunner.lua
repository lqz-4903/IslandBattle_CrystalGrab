-- =============================================
-- Test/TestRunner.lua — 测试入口（挂到场景中运行）
-- =============================================
-- 【运行方式】
--   在 Unity Console 中执行: TestRunner.RunAll()
--   或在任意 Lua 脚本中: require("Test.TestRunner").RunAll()
--
-- 【快捷键】
--   在游戏中按 T 键触发全部测试（需在 GameScene 的 Update 中注册）。
-- =============================================

local TF = require("Test.TestFramework")

-- 所有测试组
local groups = {
    { name = "A-单tick移动",     mod = "Test.GroupA_Move" },
    { name = "B-多tick累积",     mod = "Test.GroupB_MultiTick" },
    { name = "C-确定性验证",     mod = "Test.GroupC_Determinism" },
    { name = "D-插值系统",       mod = "Test.GroupD_Interp" },
    { name = "E-边界条件",       mod = "Test.GroupE_Edge" },
    { name = "F-权威位置校正",   mod = "Test.GroupF_Correction" },
    { name = "G-位置捕获",       mod = "Test.GroupG_Capture" },
    { name = "H-插值状态生命期", mod = "Test.GroupH_InterpState" },
    { name = "I-双玩家一致性",   mod = "Test.GroupI_Consistency" },
    { name = "J-状态转换位置",   mod = "Test.GroupJ_StateTrans" },
    { name = "K-数据流顺序",     mod = "Test.GroupK_DataFlow" },
    { name = "L-Fix64运算",      mod = "Test.GroupL_Fix64" },
    { name = "M-输入编码",       mod = "Test.GroupM_InputEncode" },
    { name = "N-物理边界",       mod = "Test.GroupN_PhysicsEdge" },
    { name = "O-确定性随机",     mod = "Test.GroupO_DetermRandom" },
    { name = "P-帧同步时序",     mod = "Test.GroupP_TickTiming" },
    { name = "Q-多玩家场景",     mod = "Test.GroupQ_MultiPlayer" },
    { name = "R-Fix64序列化",    mod = "Test.GroupR_Fix64Serialize" },
    { name = "S-统一物理路径",  mod = "Test.GroupS_UnifiedPath" },
    { name = "T-权威位置捕获",  mod = "Test.GroupT_AuthCapture" },
    { name = "U-帧执行与插值",  mod = "Test.GroupU_ExecOrder" },
    { name = "V-双路径一致性",  mod = "Test.GroupV_DualPath" },
    { name = "W-输入与移动分离", mod = "Test.GroupW_InputSep" },
    { name = "X-重构回归",      mod = "Test.GroupX_RefactorRegression" },
    { name = "Y-步长精确性",    mod = "Test.GroupY_StepLength" },
    { name = "Z-权威位置全链路", mod = "Test.GroupZ_AuthServer" },
    { name = "Y2-步长直接对比",  mod = "Test.GroupY2_CompStep" },
    { name = "DIAG-Move原始诊断", mod = "Test.GroupDiag_MoveDebug" },
    -- ★ 水晶系统（方案A：纯逻辑测试）
    { name = "Z1-水晶核心数学",  mod = "Test.GroupZ1_CrystalMath" },
    { name = "Z2-阶段状态机",    mod = "Test.GroupZ2_PhaseLogic" },
    { name = "Z3-水晶边界异常",  mod = "Test.GroupZ3_CrystalEdge" },
    { name = "Z4-水晶集成场景",  mod = "Test.GroupZ4_CrystalIntegration" },
}

local TestRunner = {}

--- 运行全部测试
function TestRunner.RunAll()
    TF.reset()
    print("\n╔══════════════════════════════════════╗")
    print("║  帧同步确定性测试 — 开始运行        ║")
    print("║  共 " .. #groups .. " 组测试                     ║")
    print("╚══════════════════════════════════════╝")

    for _, g in ipairs(groups) do
        -- ★ 清除 XLua require 缓存，确保每次运行加载最新代码
        package.loaded[g.mod] = nil
        local ok, mod = pcall(require, g.mod)
        if ok and mod and mod.run then
            local runOk, runErr = pcall(mod.run)
            if not runOk then
                print("  ⚠ " .. g.name .. " 执行异常: " .. tostring(runErr))
            end
        else
            print("  ⚠ " .. g.name .. " 加载失败: " .. tostring(mod))
        end
    end

    TF.summary()
end

--- 运行指定组
--- @param groupName string — 如 "A", "C", "F"
function TestRunner.RunGroup(groupName)
    TF.reset()
    for _, g in ipairs(groups) do
        if g.name:sub(1, 1) == groupName then
            package.loaded[g.mod] = nil
            local ok, mod = pcall(require, g.mod)
            if ok and mod and mod.run then
                pcall(mod.run)
            end
            break
        end
    end
    TF.summary()
end

--- 只跑阻塞性测试（A组 + C组）
--- 这两组有任何失败就不能联机
function TestRunner.RunBlocker()
    TestRunner.RunGroup("A")
    TestRunner.RunGroup("C")
end

--- 在 Unity 中注册快捷键（按 T 触发）
--- 调用方式：在 PlayerController 或 Main 的 Update 中加：
---   if CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.T) then
---       require("Test.TestRunner").RunAll()
---   end
function TestRunner.RegisterHotkey()
    local id = RegisterUpdate(function(dt)
        if CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.T) then
            TestRunner.RunAll()
        end
        if CS.UnityEngine.Input.GetKeyDown(CS.UnityEngine.KeyCode.Y) then
            TestRunner.RunBlocker()
        end
    end)
    print("[TestRunner] 快捷键已注册: T=全部测试, Y=阻塞性测试(A+C)")
    return id
end

return TestRunner
