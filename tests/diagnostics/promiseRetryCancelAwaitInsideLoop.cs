using Roblox;

public static class PromiseRetryCancelAwaitInsideLoop
{
    public static void Run()
    {
        while (true)
        {
            var retried = Promise.Retry(() => Promise.Resolve(0).Cancel(), 1);
            retried.Cancel();
            retried.Await(); // expect: [ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.
        }
    }
}
