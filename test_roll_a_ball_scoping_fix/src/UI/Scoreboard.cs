using System;
using System.Collections.Generic;

namespace RollABall.UI;

public sealed class Scoreboard
{
    private readonly List<string> _players = new();
    private readonly Dictionary<string, int> _scores = new();

    public IReadOnlyList<string> Players => _players;
    public IReadOnlyDictionary<string, int> Scores => _scores;
    public string? LastCollector { get; private set; }

    public void AddPlayer(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !_players.Contains(name))
        {
            _players.Add(name);
            if (!_scores.ContainsKey(name))
            {
                _scores[name] = 0;
            }
        }
    }

    public void RecordCollect(string name, int? points = 1)
    {
        AddPlayer(name);
        int score;
        if (!_scores.TryGetValue(name, out score))
        {
            score = 0;
        }

        _scores[name] = score + System.Math.Max(1, points != null ? points.Value : 1);
        LastCollector = name;
    }

    public int GetScore(string playerName)
    {
        int score;
        if (_scores.TryGetValue(playerName, out score))
        {
            return score;
        }
        return 0;
    }

    public bool TryGetWinner(int targetScore, out string winner)
    {
        foreach (var entry in _scores)
        {
            if (entry.Value >= targetScore)
            {
                winner = entry.Key;
                return true;
            }
        }

        winner = string.Empty;
        return false;
    }

    public void Reset()
    {
        _players.Clear();
        _scores.Clear();
        LastCollector = null;
    }
}
