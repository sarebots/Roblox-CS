using System.Threading.Tasks;
using Roblox;

public class PromiseRetryWithinLoop
{
    public async Task RunAsync()
    {
        while (true)
        {
            var retried = Promise.Retry(() => Promise.Resolve(0), 1);
            await retried; // expect: [ROBLOXCS3025] Awaiting a Promise inside a loop is not supported. Use Promise.Await() or restructure control flow.
        }
    }
}
