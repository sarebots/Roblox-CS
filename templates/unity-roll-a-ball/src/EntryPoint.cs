using Roblox;
using __PROJECT_NAMESPACE__.Game;

namespace __PROJECT_NAMESPACE__;

public static class EntryPoint
{
    public static void Main()
    {
        var projectName = "__PROJECT_NAME__";
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
