using Roblox;
using __PROJECT_NAMESPACE__.Game;

namespace __PROJECT_NAMESPACE__;

public static class EntryPoint
{
    public static void Main()
    {
        var projectName = "__PROJECT_NAME__";
        var loop = new GameLoop();
        var winner = Promise.Await(loop.PlayRoundAsync());
        var score = loop.GetScore(winner);
        System.Console.WriteLine($"Bootstrapping {projectName}... {winner} reached {score} points!");
    }
}
