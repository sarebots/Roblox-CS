using Roblox;

public static class PromiseAwaitStatusUnsupported
{
    public static void Run()
    {
        var promise = Promise.Resolve(5);
        promise.AwaitStatus(); // expect: [ROBLOXCS3030] Awaiting a Promise using AwaitStatus() is not supported. Use Promise.AwaitResult instead.
    }
}
