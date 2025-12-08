using Roblox;

public static class PromiseRetryAwaitCall
{
    public static void Run()
    {
        var retried = Promise.Retry(() => Promise.Resolve(0), 2);
        retried.Await(); // expect: [ROBLOXCS3029] Awaiting a Promise.Retry result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
