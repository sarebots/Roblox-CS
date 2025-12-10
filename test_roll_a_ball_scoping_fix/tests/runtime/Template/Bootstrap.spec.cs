using Roblox;
using RollABall.Game;

namespace RollABall.Runtime;

public static class TemplateBootstrapSpec
{
    public static void ShouldResolveAsyncWinCondition()
    {
        var loop = new GameLoop();
        var winnerPromise = loop.PlayRoundAsync(targetCollects: 3, tickDelta: 0.4f, delaySeconds: 0.01);
        var timedPromise = Promise.Timeout(winnerPromise, 5.0, "Roll-a-Ball round timed out");
        var winner = Promise.Await(timedPromise);

        if (string.IsNullOrEmpty(winner))
        {
            throw new System.Exception("Expected Promise to resolve with winning player.");
        }

        var score = loop.GetScore(winner);
        if (score < 3)
        {
            throw new System.Exception($"Expected {winner} to accumulate at least 3 points, but only has {score}.");
        }

        if (loop.IsRunning)
        {
            throw new System.Exception("GameLoop should stop automatically once a winner is declared.");
        }
    }

    public static void ShouldAccumulateScoreOnManualTicks()
    {
        var loop = new GameLoop();
        loop.Start();
        for (var index = 0; index < 6; index++)
        {
            loop.Tick(0.5f);
        }

        loop.Stop();

        var activePlayers = loop.ActivePlayers;
        if (activePlayers.Count == 0)
        {
            throw new System.Exception("Expected scoreboard to contain collected player entries.");
        }

        var runnerScore = loop.GetScore("Runner");
        if (runnerScore <= 0)
        {
            throw new System.Exception("Expected manual ticking to increment the runner score.");
        }
    }
}
