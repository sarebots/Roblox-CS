using System.Threading.Tasks;
using Roblox;

class PromiseTelemetryHelpers
{
    public void IgnoredHelper()
    {
        Promise.Timeout(Promise.Resolve(0), 1); // expect: [ROBLOXCS3017] Promise helper results must be awaited, returned, or chained with Catch to avoid unhandled rejection telemetry.
    }

    public async Task AwaitedHelperAsync()
    {
        await Promise.Timeout(Promise.Resolve(0), 1);
    }

    public Promise<int> ReturnedHelper() => Promise.Retry(() => Promise.Resolve(5), 2);

    public void ChainedHelper()
    {
        Promise.RetryWithDelay(() => Promise.Resolve(1), 2, _ => 0).Catch(_ => { });
    }
}
