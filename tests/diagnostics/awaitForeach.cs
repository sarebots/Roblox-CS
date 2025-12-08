using System.Collections.Generic;
using System.Threading.Tasks;

public static class AwaitForeach
{
    public static async Task Run(IAsyncEnumerable<int> values)
    {
        await foreach (var value in values) // expect: [ROBLOXCS3019] await foreach loops are not supported.
        {
        }
    }
}
