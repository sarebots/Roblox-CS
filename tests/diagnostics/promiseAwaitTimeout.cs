using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitTimeout
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var timedOut = Promise.Timeout(promise, 1);
        await timedOut; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
