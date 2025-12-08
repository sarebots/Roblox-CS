using Roblox;

public static class PromiseRetryAwaitValueCall
{
    public static void Run()
    {
        var retried = Promise.Retry(() => Promise.Resolve(0), 2);
        retried.AwaitValue(); // expect: [ROBLOXCS3029] Awaiting a Promise.Retry result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
