using System.Threading.Tasks;
using Roblox;

public class PromiseTimeoutThenAwait
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var chained = Promise.Timeout(promise, 1).Then(value => value);
        await chained; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
