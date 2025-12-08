using System;
using System.Threading.Tasks;

public class AwaitInsideSyncLambda
{
    public Task Run()
    {
        Func<Task> fn = () =>
        {
            await Task.CompletedTask; // expect: [ROBLOXCS3018] Await expressions inside lambda expressions require the 'async' modifier.
            return Task.CompletedTask;
        };

        return fn();
    }
}
