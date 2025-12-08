using Roblox;

public static class PromiseRetryCancelAwait
{
    public static void Run()
    {
        var promise = Promise.Resolve(0);
        var retried = Promise.Retry(() => promise.Cancel(), 2);
        _ = retried.Await(); // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
