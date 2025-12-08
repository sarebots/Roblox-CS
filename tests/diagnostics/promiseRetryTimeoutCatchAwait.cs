using System.Threading.Tasks;
using Roblox;

public class PromiseRetryTimeoutCatchAwait
{
    public async Task RunAsync()
    {
        var retried = Promise.Retry(
            () => Promise.Timeout(Promise.Resolve(0), 1).Catch(err => Promise.Resolve(1)),
            2
        );

        await retried; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
