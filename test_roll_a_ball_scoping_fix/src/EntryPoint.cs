using Roblox;
using RollABall.Game;

namespace RollABall;

public static class EntryPoint
{
    public static void Main()
    {
        var projectName = "RollABall";
        var loop = new GameLoop();
        var result = Promise.GetAwaitResult(loop.PlayRoundAsync());
        var winner = result.Value;
        if (winner == null)
        {
            winner = "Unknown";
        }
        var score = loop.GetScore(winner);
        Roblox.Globals.print($"Bootstrapping {projectName}... {winner} reached {score} points!");
    }
}
