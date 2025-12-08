using System.Threading.Tasks;

public class AwaitInsideSync
{
    public void Run()
    {
        await Task.CompletedTask; // expect: [ROBLOXCS3018] Await expressions inside method bodies require the 'async' modifier.
    }
}
