using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitThenCancel
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        var chained = promise.Then(value => value);
        chained.Cancel();
        await chained; // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
