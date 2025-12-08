using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitWithoutHandling
{
    public async Task<int> RunAsync()
    {
        var promise = Promise.Resolve(5);
        return await promise; // expect: [ROBLOXCS3026] Awaiting a Promise directly is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
