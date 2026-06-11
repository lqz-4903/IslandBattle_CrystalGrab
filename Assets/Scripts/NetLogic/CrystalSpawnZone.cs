using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     CrystalSpawnZone —— 水晶生成区域（挂载到场景空节点）
/// ═══════════════════════════════════════════════════════════════
///
/// 【用途】
///   在 GameScene 中放置 5 个空 GameObject，挂载此组件。
///   游戏开始时 GameEventHandler 扫描场景，收集所有区域配置。
///   每个区域在生成阶段独立产出水晶。
///
/// 【配置】
///   Radius —— 在编辑器中拖拽调整，所见即所得。
///   Transform.position —— 区域圆心（由节点世界坐标决定）。
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public class CrystalSpawnZone : MonoBehaviour
{
    [Tooltip("生成半径（米），水晶在此圆内随机位置生成")]
    public float Radius = 8f;

    /// <summary>区域中心（运行时从 worldPosition 读取，生成时用 Fix64）</summary>
    public Vector3 Center => transform.position;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 半透明圆盘
        Gizmos.color = new Color(0f, 1f, 0.8f, 0.15f);
        Gizmos.DrawSphere(transform.position, Radius);
        // 圆形轮廓
        Gizmos.color = new Color(0f, 1f, 0.8f, 0.6f);
        UnityEditor.Handles.Disc(Quaternion.identity, transform.position,
            Vector3.up, Radius, false, 0f);
        // 中心十字
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position + Vector3.left * 0.5f,
                        transform.position + Vector3.right * 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.forward * 0.5f,
                        transform.position + Vector3.back * 0.5f);
    }
#endif
}
