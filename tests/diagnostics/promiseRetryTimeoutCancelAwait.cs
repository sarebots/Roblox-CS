using Roblox;

public static class PromiseRetryTimeoutCancelAwait
{
    public static void Run()
    {
        var retried = Promise.Retry(
            () => Promise.Timeout(Promise.Resolve(0), 1)
                .Catch(err => Promise.Resolve(2))
                .Then(v => Promise.Resolve(v).Cancel()),
            1
        );

        retried.Await(); // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
