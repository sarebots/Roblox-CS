using System.Threading.Tasks;
using Roblox;

public class PromiseAwaitCancelReturn
{
    public async Task RunAsync()
    {
        var promise = Promise.Resolve(0);
        await promise.Cancel(); // expect: [ROBLOXCS3024] Awaiting the result of Promise.Cancel() is not supported.
    }
}
