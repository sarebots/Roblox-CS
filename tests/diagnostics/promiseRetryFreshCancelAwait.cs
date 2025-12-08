using Roblox;

public static class PromiseRetryFreshCancelAwait
{
    public static void Run()
    {
        var retried = Promise.Retry(() => Promise.Resolve(0).Then(v => Promise.Resolve(v).Cancel()), 1);
        retried.Await(); // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
