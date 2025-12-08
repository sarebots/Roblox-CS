using Roblox;

public static class PromiseTimeoutAwaitValueOrNilCall
{
    public static void Run()
    {
        var timedOut = Promise.Timeout(Promise.Resolve(0), 1);
        timedOut.AwaitValueOrNil(); // expect: [ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
