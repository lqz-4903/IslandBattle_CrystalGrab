using UnityEngine;
using GameProto;

/// <summary>
/// 单机集成测试 —— 挂到 BeginScene 的空 GameObject 上
/// 需要场景中有 HostServer + NetMgr + KcpMgr 组件
/// </summary>
public class FrameworkTest : MonoBehaviour
{
    private HostServer _host;
    private bool _gameStarted;
    private float _testTimer;
    private int _testStep;

    void Start()
    {
        _host = HostServer.Instance;
        if (_host == null)
        {
            Debug.LogError("场景中没有 HostServer！");
            return;
        }

        Debug.Log("===== 测试开始 =====");

        // 第1步：启动主机，创建房间
        _host.StartHost("TestHost", 8888);
    }

    void Update()
    {
        if (_host == null || !_host.IsRunning) return;

        // ====== 阶段1：等房间创建完，模拟第二个玩家加入 ======
        if (!_gameStarted && _host.CurrentRoom != null && _host.CurrentRoom.ConvToPlayer.Count < 2)
        {
            // 模拟一个假客户端加入（conv=9999，不走真实网络）
            var fakeJoin = new JoinRoom
            {
                RoomId = _host.CurrentRoom.RoomId,
                PlayerName = "TestClient"
            };
            EventCenter.Dispatch(12, 9999u, fakeJoin);
            Debug.Log("[测试] 模拟玩家2加入");
        }

        // ====== 阶段2：有2人后，开始游戏 ======
        if (!_gameStarted && _host.CurrentRoom != null && _host.CurrentRoom.ConvToPlayer.Count >= 2)
        {
            var startMsg = new GameStart();
            EventCenter.Dispatch(16, RoomData.HostConv, startMsg);
            Debug.Log("[测试] 派发GameStart");
        }

        // ====== 阶段3：游戏开始后，执行各项测试 ======
        if (_host.IsGameStarted && !_gameStarted)
        {
            _gameStarted = true;
            _testTimer = 0f;
            _testStep = 0;
            Debug.Log("===== 游戏已开始，开始测试 =====");
        }

        if (!_gameStarted) return;

        _testTimer += Time.deltaTime;

        // 每隔1秒执行一个测试步骤
        if (_testTimer < 1f) return;
        _testTimer = 0f;
        _testStep++;

        int hostId = _host.CurrentRoom.HostPlayerId;

        switch (_testStep)
        {
            case 1:
                // 测试：提交主机玩家输入
                _host.SubmitHostInput(
                    moveDir: 0b0001,  // W键
                    jump: true,
                    attack: false,
                    skill: false,
                    cameraYaw: 45f,
                    chargeTime: 0f
                );
                Debug.Log("[测试1] 提交主机输入 moveDir=1(W) jump=true");
                break;

            case 2:
                // 测试：手动触发水晶拾取（先绕过 activeCrystals 校验，直接测加分逻辑）
                // 注意：由于 SpawnCrystal 是 TODO，这里直接模拟一个已存在的水晶
                Debug.Log("[测试2] 跳过水晶拾取测试（SpawnCrystal是TODO，activeCrystals为空）");
                break;

            case 3:
                // 测试：模拟玩家受击（用主机玩家作为 victim）
                var hit = new PlayerHit
                {
                    AttackerId = 2,
                    VictimId = hostId,
                    DroppedCount = 1
                };
                EventCenter.Dispatch(32, 0u, hit);
                Debug.Log("[测试3] 派发PlayerHit victim=主机 掉落1颗");
                break;

            case 4:
                // 测试：模拟玩家坠落
                var fall = new PlayerFall
                {
                    PlayerId = hostId,
                    DroppedCount = 2
                };
                EventCenter.Dispatch(33, 0u, fall);
                Debug.Log("[测试4] 派发PlayerFall 玩家=主机 掉落2颗");
                break;

            case 5:
                // 测试：模拟玩家重生（HP应该还剩1，>0所以可以重生）
                var respawn = new PlayerRespawn
                {
                    PlayerId = hostId,
                    PosX = 0f,
                    PosY = 1f,
                    PosZ = 0f
                };
                EventCenter.Dispatch(35, 0u, respawn);
                Debug.Log("[测试5] 派发PlayerRespawn 玩家=主机");
                break;

            case 6:
                // 测试：再坠落一次，HP=0，应该被淘汰
                var fall2 = new PlayerFall
                {
                    PlayerId = hostId,
                    DroppedCount = 0
                };
                EventCenter.Dispatch(33, 0u, fall2);
                Debug.Log("[测试6] 再次坠落 HP应为0 触发淘汰");
                break;

            case 7:
                // 测试：查询最终状态
                Debug.Log("[测试7] 主机玩家 HP=" + _host.GameEventHandler?.GetPlayerHP(hostId)
                         + " Score=" + _host.GameEventHandler?.GetPlayerScore(hostId));
                Debug.Log("===== 全部测试完成 =====");
                break;
        }
    }
}
