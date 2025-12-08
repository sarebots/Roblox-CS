using System.Threading.Tasks;

public class AwaitInFinally
{
    public async Task Run()
    {
        try
        {
        }
        finally
        {
            await Task.CompletedTask; // expect: [ROBLOXCS3023] Await expressions are not supported inside finally blocks. Move the await outside the finally block.
        }
    }
}
