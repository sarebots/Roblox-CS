using Roblox;

public static class PromiseRetryAwaitStatusCall
{
    public static void Run()
    {
        var retried = Promise.Retry(() => Promise.Resolve(0), 2);
        retried.AwaitStatus(); // expect: [ROBLOXCS3029] Awaiting a Promise.Retry result is not supported. Use Promise.Await()/PromiseAwaitResult instead.
    }
}
