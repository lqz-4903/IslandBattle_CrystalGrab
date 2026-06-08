using GameProto;
using Google.Protobuf;
using UnityEngine;

/// <summary>
/// 游戏倒计时管理器（客户端侧）
/// 接收服务端广播的 GameTimerUpdate，供 Lua 层读取
/// </summary>
public class GameTimerManager : MonoBehaviour
{
    private static GameTimerManager _instance;
    public static GameTimerManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("GameTimerManager");
                _instance = go.AddComponent<GameTimerManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    // 剩余时间（秒），由服务端权威更新
    private float _remainingTime;
    public float RemainingTime => _remainingTime;

    // 是否已收到计时器数据
    private bool _hasReceived;
    public bool HasReceived => _hasReceived;

    // 游戏是否已结束
    private bool _isGameEnd;
    public bool IsGameEnd => _isGameEnd;

    // 胜者名字
    private string _winnerName = "";
    public string WinnerName => _winnerName;

    private void OnEnable()
    {
        // 监听服务端广播的倒计时更新（msgId = 36）
        EventCenter.AddListener(36, OnTimerUpdate);
        // 监听游戏结束（msgId = 34）
        EventCenter.AddListener(34, OnGameEnd);
    }

    private void OnDisable()
    {
        EventCenter.RemoveListener(36, OnTimerUpdate);
        EventCenter.RemoveListener(34, OnGameEnd);
    }

    /// <summary>
    /// 收到服务端倒计时更新
    /// </summary>
    private void OnTimerUpdate(IMessage msg)
    {
        if (msg is GameTimerUpdate timerMsg)
        {
            _remainingTime = timerMsg.RemainingTime;
            _hasReceived = true;
        }
    }

    /// <summary>
    /// 收到游戏结束
    /// </summary>
    private void OnGameEnd(IMessage msg)
    {
        _isGameEnd = true;
        if (msg is GameEnd gameEnd)
        {
            _winnerName = gameEnd.WinnerName;
        }
    }

    /// <summary>
    /// 重置状态（进入新游戏时调用）
    /// </summary>
    public void Reset()
    {
        _remainingTime = 0;
        _hasReceived = false;
        _isGameEnd = false;
        _winnerName = "";
    }
}
