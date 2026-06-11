using System;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     CrystalComponent —— 水晶 GameObject 标识组件 + 触发拾取
/// ═══════════════════════════════════════════════════════════════
///
/// 【用途】
///   1. 存储水晶 ID（Lua 侧通过 GetComponent 读取）
///   2. 添加 SphereCollider(isTrigger=true) + OnTriggerEnter，
///      角色（CharacterController）进入范围时回调 Lua 执行拾取。
///
/// 【触发流程】
///   CharacterController 进入 → OnTriggerEnter
///     → LastCrystalId / LastPlayerGo 写入静态字段
///     → OnPlayerEnterTrigger()
///     → Lua CrystalManager 读取静态字段，检查是否为本地玩家
///     → 是本地玩家：回收水晶 + 发送拾取请求到服务端
///
/// 【注意】
///   - 回调使用无参 System.Action，因为 int 是值类型，XLua 泛型桥不支持
///     Action&lt;int, GameObject&gt;（需加入 CSharpCallLua 生成列表）。
///     改用静态字段传参，Unity 主线程执行无竞态。
///   - 水晶不应有 MeshCollider（会导致与角色的物理碰撞）
///   - 拾取范围由 SphereCollider.radius 控制
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public class CrystalComponent : MonoBehaviour
{
    public int CrystalId;

    /// <summary>Lua 回调：玩家进入触发范围时调用（无参，从静态字段读取数据）</summary>
    public static Action OnPlayerEnterTrigger;

    /// <summary>最近触发的水晶 ID（Lua 侧读取）</summary>
    public static int LastCrystalId;

    /// <summary>最近触发事件的玩家 GameObject（Lua 侧读取）</summary>
    public static GameObject LastPlayerGo;

    private void OnTriggerEnter(Collider other)
    {
        // CharacterController 自带 CapsuleCollider，能与 isTrigger 碰撞体产生事件
        if (other.GetComponent<CharacterController>() != null)
        {
            LastCrystalId = CrystalId;
            LastPlayerGo = other.gameObject;
            OnPlayerEnterTrigger?.Invoke();
        }
    }
}
