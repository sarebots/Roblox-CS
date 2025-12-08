using Roblox;

public static class PromiseAwaitValueUnsupported
{
    public static void Run()
    {
        var promise = Promise.Resolve(5);
        promise.AwaitValue(); // expect: [ROBLOXCS3030] Awaiting a Promise using AwaitValue() is not supported. Use Promise.AwaitResult instead.
    }
}
