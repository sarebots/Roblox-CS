using Roblox;

public static class IteratorHelperDiagnostics
{
    public static void Sample()
    {
        foreach (var value in TS.iter(new[] { 1, 2 }))
        {
            _ = value; // expect: [ROBLOXCS3032] TS.iter/TS.array_flatten are disabled. Enable Macro.EnableIteratorHelpers in roblox-cs.yml or pass --macro-iterator-helpers=true.
        }
    }

    public static void Flatten()
    {
        foreach (var value in TS.array_flatten(new[] { new[] { 1, 2 } }))
        {
            _ = value; // expect: [ROBLOXCS3032] TS.iter/TS.array_flatten are disabled. Enable Macro.EnableIteratorHelpers in roblox-cs.yml or pass --macro-iterator-helpers=true.
        }
    }
}
