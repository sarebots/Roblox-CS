using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitInLoop
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);

        while (true)
        {
            await promise; // expect: [ROBLOXCS3025] Awaiting a Promise inside a loop is not supported. Use Promise.Await() or restructure control flow.
        }
    }
}
