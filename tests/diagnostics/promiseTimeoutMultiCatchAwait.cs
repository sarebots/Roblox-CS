using System.Threading.Tasks;
using Roblox;

public class PromiseTimeoutMultiCatchAwait
{
    public async Task RunAsync()
    {
        var promise = Promise.Timeout(Promise.Resolve(0), 1)
            .Catch(err => Promise.Resolve(1))
            .Catch(err => Promise.Resolve(2));

        await promise; // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
