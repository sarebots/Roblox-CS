using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitAfterCancel
{
    public async Task<int> RunAsync()
    {
        var promise = Promise.Resolve(5);
        promise.Cancel();
        return await promise; // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
