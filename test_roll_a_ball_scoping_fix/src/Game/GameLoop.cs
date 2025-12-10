using System.Collections.Generic;
using Roblox;

namespace RollABall.Game;

public sealed class GameLoop
{
    private readonly PlayerController _playerController = new();
    private readonly UI.Scoreboard _scoreboard = new();
    private float _elapsed;
    private bool _running;

    private const string DefaultPlayerName = "Runner";

    public IReadOnlyList<string> ActivePlayers => _scoreboard.Players;
    public IReadOnlyDictionary<string, int> Scores => _scoreboard.Scores;
    public bool IsRunning => _running;
    public string? LastCollector => _scoreboard.LastCollector;

    public void Start(string playerName = DefaultPlayerName)
    {
        _playerController.OnCollect -= HandleCollect;
        _scoreboard.Reset();
        _scoreboard.AddPlayer(playerName);
        _playerController.ResetPlayer();
        _playerController.OnCollect += HandleCollect;
        _elapsed = 0f;
        _running = true;
    }

    public Promise<string> PlayRoundAsync(int targetCollects = 3, float tickDelta = 0.5f, double delaySeconds = 0.05)
    {
        Start();
        return WaitForWinnerAsync(targetCollects, tickDelta, delaySeconds);
    }

    public void Tick(float deltaTime)
    {
        if (!_running)
        {
            return;
        }

        _elapsed += deltaTime;
        _playerController.Update(deltaTime);
    }

    public void Stop(bool clearScores = false)
    {
        _running = false;
        _playerController.OnCollect -= HandleCollect;
        if (clearScores)
        {
            _scoreboard.Reset();
        }
        _elapsed = 0f;
    }

    public int GetScore(string playerName)
    {
        return _scoreboard.GetScore(playerName);
    }

    private Promise<string> WaitForWinnerAsync(int targetCollects, float tickDelta, double delaySeconds)
    {
        if (!_running)
        {
            return Promise.Reject<string>("GameLoop has been stopped.");
        }

        if (_scoreboard.TryGetWinner(targetCollects, out var winner))
        {
            Stop(clearScores: false);
            return Promise.Resolve(winner);
        }

        Tick(tickDelta);
        return Promise.Delay(delaySeconds, true)
            .Then(_ => WaitForWinnerAsync(targetCollects, tickDelta, delaySeconds));
    }

    private void HandleCollect(string playerName)
    {
        _scoreboard.RecordCollect(playerName);
    }
}
