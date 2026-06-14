using System;
using UnityEngine;
using XLua;

/// <summary>
/// 挂载在箭矢 GameObject 上，检测与玩家 CharacterController 的 Trigger 碰撞。
/// 触发时回调 Lua 侧 Arrow.OnTriggerEnterPlayer(arrowGo, targetPlayerId)。
///
/// ★ 使用 LuaFunction 而非 Action&lt;GameObject, int&gt;：
///   Action&lt;T1, T2&gt; 是多参数泛型委托，XLua 需要在 CSharpCallLua 中显式注册才能
///   将 Lua 函数赋值给它。改用 LuaFunction 作为参数传入，XLua 原生支持该类型，
///   无需额外生成代码。
/// </summary>
public class ArrowTrigger : MonoBehaviour
{
    /// <summary>Lua 侧注册的回调函数（LuaFunction 类型，XLua 原生支持）</summary>
    private static LuaFunction _onArrowHitPlayer;

    /// <summary>
    /// ★ Lua 侧调用此方法注册回调：CS.ArrowTrigger.SetCallback(function(arrowGo, playerId) ... end)
    /// </summary>
    public static void SetCallback(LuaFunction func)
    {
        if (_onArrowHitPlayer != null)
        {
            _onArrowHitPlayer.Dispose();
        }
        _onArrowHitPlayer = func;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 查找被命中玩家的 PlayerTag 组件
        var playerTag = other.GetComponentInParent<PlayerTag>();
        if (playerTag == null) return;

        int targetPlayerId = playerTag.PlayerId;
        if (_onArrowHitPlayer != null)
        {
            _onArrowHitPlayer.Call(gameObject, targetPlayerId);
        }
    }
}
