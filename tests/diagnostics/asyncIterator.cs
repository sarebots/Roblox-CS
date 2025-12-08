using System.Collections.Generic;
using System.Threading.Tasks;

public class AsyncIterator
{
    public async IAsyncEnumerable<int> Values()
    {
        yield return 1; // expect: [ROBLOXCS3020] Async iterator methods are not supported.
    }
}
