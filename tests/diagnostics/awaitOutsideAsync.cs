using System.Threading.Tasks;

public class AwaitOutsideAsync
{
    public Task Run()
    {
        await Task.CompletedTask; // expect: [ROBLOXCS3018] Await expressions inside method bodies require the 'async' modifier.
        return Task.CompletedTask;
    }
}
