using System.Threading.Tasks;
using Roblox;

public class PromiseRetryTimeoutAwait
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var retried = Promise.Retry(() => Promise.Timeout(promise, 1), 2);
        await retried; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
