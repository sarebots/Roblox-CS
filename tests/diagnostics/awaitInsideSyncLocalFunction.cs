using System.Threading.Tasks;

class AwaitInsideSyncLocalFunction
{
    public Task<int> ComputeAsync()
    {
        Task<int> Local()
        {
            await Task.CompletedTask; // expect: [ROBLOXCS3018] Await expressions inside local functions require the 'async' modifier.
            return Task.FromResult(1);
        }

        return Local();
    }
}
