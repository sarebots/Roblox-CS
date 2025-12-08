using System.Threading.Tasks;
using Roblox;

public class PromiseRetryAwait
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var retried = Promise.Retry(() => promise, 3);
        await retried; // expect: [ROBLOXCS3029] Awaiting a Promise.Retry result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
