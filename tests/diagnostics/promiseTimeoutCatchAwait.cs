using System.Threading.Tasks;
using Roblox;

public class PromiseTimeoutCatchAwait
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var timeoutCaught = Promise.Timeout(promise, 1).Catch(err => Promise.Resolve(1));
        await timeoutCaught; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
