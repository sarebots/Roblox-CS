using System.Threading.Tasks;
using Roblox;

public class PromiseRetryTimeoutLoopAwait
{
    public async Task RunAsync()
    {
        while (true)
        {
            var retriedTimeout = Promise.Retry(() => Promise.Timeout(Promise.Resolve(0), 1), 3);
            await retriedTimeout; // expect: [ROBLOXCS3025] Awaiting a Promise inside a loop is not supported. Use Promise.Await() or restructure control flow.
        }
    }
}
