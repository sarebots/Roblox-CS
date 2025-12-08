using Roblox;

public static class PromiseDoubleCancel
{
    public static void Run()
    {
        var promise = Promise.Resolve(1);
        promise.Cancel();
        promise.Cancel(); // expect: [ROBLOXCS3031] Cancelling the same Promise more than once is not supported.
    }
}
