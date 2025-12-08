using Roblox;

public static class PromiseAwaitResultUnsupported
{
    public static void Run()
    {
        var promise = Promise.Resolve(5);
        promise.AwaitResult(); // expect: [ROBLOXCS3030] Awaiting a Promise using AwaitResult() is not supported. Use Promise.AwaitResult instead.
    }
}
