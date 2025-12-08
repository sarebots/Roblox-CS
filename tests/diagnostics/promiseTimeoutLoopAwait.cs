using System.Threading.Tasks;
using Roblox;

public class PromiseTimeoutLoopAwait
{
    public async Task RunAsync()
    {
        while (true)
        {
            var timed = Promise.Timeout(Promise.Resolve(0), 1);
            await timed; // expect: [ROBLOXCS3025] Awaiting a Promise inside a loop is not supported. Use Promise.Await() or restructure control flow.
        }
    }
}
