using Roblox;

public static class PromiseAwaitValueOrNilUnsupported
{
    public static void Run()
    {
        var promise = Promise.Resolve(5);
        promise.AwaitValueOrNil(); // expect: [ROBLOXCS3030] Awaiting a Promise using AwaitValueOrNil() is not supported. Use Promise.AwaitResult instead.
    }
}
