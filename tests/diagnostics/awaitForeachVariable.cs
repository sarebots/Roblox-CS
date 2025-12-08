using System.Collections.Generic;
using System.Threading.Tasks;

public static class AwaitForeachVariable
{
    public static async Task Run(IAsyncEnumerable<(int first, int second)> values)
    {
        await foreach (var (first, second) in values) // expect: [ROBLOXCS3019] await foreach loops are not supported.
        {
            _ = first + second;
        }
    }
}
