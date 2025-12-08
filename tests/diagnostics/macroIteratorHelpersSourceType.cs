using Roblox;

public static class IteratorHelperSourceType
{
    public static void Sample()
    {
        foreach (var value in TS.iter(123))
        {
            _ = value; // expect: [ROBLOXCS3034] TS.iter requires an array or IEnumerable source.
        }
    }
}
