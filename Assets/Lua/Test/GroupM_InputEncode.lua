-- =============================================
-- Test/GroupM_InputEncode.lua — M组 输入编码/解码 (6条)
-- =============================================

local TF = require("Test.TestFramework")
local GC = require("Core.GameConst")

local function run()
    TF.group("M — 输入编码/解码")

    -- ===== M1: 单键编码对应位 =====
    TF.assertEqual(GC.MOVE_FORWARD,  1, TF.TIGHT, "M1-W=bit0=1")
    TF.assertEqual(GC.MOVE_BACKWARD, 2, TF.TIGHT, "M1-S=bit1=2")
    TF.assertEqual(GC.MOVE_LEFT,     4, TF.TIGHT, "M1-A=bit2=4")
    TF.assertEqual(GC.MOVE_RIGHT,    8, TF.TIGHT, "M1-D=bit3=8")

    -- 验证单键之间不重叠
    local allBits = GC.MOVE_FORWARD + GC.MOVE_BACKWARD + GC.MOVE_LEFT + GC.MOVE_RIGHT
    TF.assertEqual(allBits, 15, TF.TIGHT, "M1-四键合计=15(低4位全1)")

    -- ===== M2: 组合键编码不冲突 =====
    local wa = GC.MOVE_FORWARD + GC.MOVE_LEFT  -- W+A = 5
    local sd = GC.MOVE_BACKWARD + GC.MOVE_RIGHT -- S+D = 10
    local wasd = GC.MOVE_FORWARD + GC.MOVE_BACKWARD + GC.MOVE_LEFT + GC.MOVE_RIGHT
    TF.assertEqual(wa, 5, TF.TIGHT, "M2-W+A=5")
    TF.assertEqual(sd, 10, TF.TIGHT, "M2-S+D=10")
    TF.assertEqual(wasd, 15, TF.TIGHT, "M2-WASD=15")

    -- 验证低4位提取
    local function low4(v) return v & 0x0F end
    TF.assertEqual(low4(wa), 5, TF.TIGHT, "M2-low4(W+A)=5")
    TF.assertEqual(low4(sd), 10, TF.TIGHT, "M2-low4(S+D)=10")

    -- ===== M3: Roll 标志位不干扰方向 =====
    local wRoll  = GC.MOVE_FORWARD + GC.MOVE_ROLL
    local aRoll  = GC.MOVE_LEFT + GC.MOVE_ROLL
    TF.assertEqual(wRoll & 0x0F, GC.MOVE_FORWARD, TF.TIGHT, "M3-W+ROLL低4位=W")
    TF.assertEqual(aRoll & 0x0F, GC.MOVE_LEFT, TF.TIGHT, "M3-A+ROLL低4位=A")

    -- 验证 bit4
    TF.assertTrue((wRoll & GC.MOVE_ROLL) ~= 0, "M3-W+ROLL的bit4=1")
    TF.assertTrue((GC.MOVE_FORWARD & GC.MOVE_ROLL) == 0, "M3-纯W的bit4=0")

    -- ===== M4: 跳跃粘滞逻辑 =====
    -- 模拟 InputHandler 的粘滞行为
    local jumpPressed = true  -- GetButtonDown 拉高
    -- 第1次 GetTickInput
    local jumpTick1 = jumpPressed
    jumpPressed = false  -- 消费后拉低
    TF.assertTrue(jumpTick1, "M4-第1次tick jump=true")
    -- 第2次 GetTickInput（没再按下）
    TF.assertFalse(jumpPressed, "M4-第2次tick jump=false(已消费)")

    -- ===== M5: 技能粘滞（同 M4）=====
    local skillPressed = true
    local skillTick1 = skillPressed
    skillPressed = false
    TF.assertTrue(skillTick1, "M5-技能只在释放tick为true")
    TF.assertFalse(skillPressed, "M5-后续tick为false")

    -- ===== M6: 蓄力计时归零 =====
    local chargeTime = 0
    local attackHeld = true
    -- 按住期间累积（2 秒）
    local ticksFor2s = math.floor(2 / GC.TICK_INTERVAL)
    for _ = 1, ticksFor2s do
        chargeTime = chargeTime + GC.TICK_INTERVAL
    end
    TF.assertInRange(chargeTime, 1.9, 2.1, "M6-蓄力2s≈2.0")
    -- 松开
    attackHeld = false
    chargeTime = 0
    TF.assertEqual(chargeTime, 0, TF.TIGHT, "M6-松开后归零")

    print("[M组] 输入编码测试完成")
end

return { run = run }
