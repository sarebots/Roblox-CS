using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Roblox;

namespace RuntimeSpecs;

public sealed class PromiseTestSignal
{
    private readonly List<Action<string>> _callbacks = new();
    private readonly List<string> _queued = new();

    public int GetListenerCount() => _callbacks.Count;

    public void Queue(string value)
    {
        _queued.Add(value);
    }

    public Action Connect(Action<string> callback)
    {
        while (_queued.Count > 0)
        {
            var buffered = _queued[0];
            _queued.RemoveAt(0);
            callback(buffered);
        }

        _callbacks.Add(callback);
        return () => _callbacks.Remove(callback);
    }

    public void Fire(string value)
    {
        for (var i = 0; i < _callbacks.Count; i++)
        {
            _callbacks[i](value);
        }
    }
}

public static class PromiseBasicSpec
{
    public static void ShouldAllowAsyncFunctionDeclarations()
    {
        async Task<string> Foo() => "foo";
        async Task<string> Bar() => (await Foo()) + "bar";

        var result = Promise.GetAwaitResult(Bar());
        if (!result.Success || result.Value is not "foobar")
        {
            throw new Exception($"Async function declaration returned unexpected result: {result.Value}");
        }
    }

    public static void ShouldAllowAsyncAnonymousMethods()
    {
        Func<Task<string>> foo = async delegate { return "foo"; };
        Func<Task<string>> bar = async delegate { return (await foo()) + "bar"; };

        var result = Promise.GetAwaitResult(bar());
        if (!result.Success || result.Value is not "foobar")
        {
            throw new Exception($"Async anonymous method returned unexpected result: {result.Value}");
        }
    }

    public static void ShouldAllowAsyncLambdaExpressions()
    {
        Func<Task<string>> foo = async () => "foo";
        Func<Task<string>> bar = async () => (await foo()) + "bar";

        var result = Promise.GetAwaitResult(bar());
        if (!result.Success || result.Value is not "foobar")
        {
            throw new Exception($"Async lambda returned unexpected result: {result.Value}");
        }
    }

    public static void ShouldAllowAsyncStaticClassMethods()
    {
        var result = Promise.GetAwaitResult(RuntimeSpecs.AsyncStaticExample.Bar());
        if (!result.Success || result.Value is not "foobar")
        {
            throw new Exception($"Async static method returned unexpected result: {result.Value}");
        }
    }

    public static void ShouldAllowAsyncInstanceMethods()
    {
        var instance = new RuntimeSpecs.AsyncInstanceExample();
        var result = Promise.GetAwaitResult(instance.Bar());
        if (!result.Success || result.Value is not "foobar")
        {
            throw new Exception($"Async instance method returned unexpected result: {result.Value}");
        }
    }

    public static void ShouldNotRunCodeAfterCancellation()
    {
        var ranContinuation = false;

        var promise = Promise.Delay(0.05)
            .Then<object?>(_ =>
            {
                ranContinuation = true;
                return Promise.Resolve<object?>(null);
            });

        Promise.Cancel(promise);

        var result = Promise.GetAwaitResult(promise);
        if (result.Success)
        {
            throw new Exception("Promise should reject after cancellation.");
        }

        if (ranContinuation)
        {
            throw new Exception("Promise continuation ran after cancellation.");
        }
    }

    public static void ShouldResolvePromise()
    {
        var promise = Promise.Resolve<string>("foo");
        var result = Promise.GetAwaitResult(promise);
        if (!result.Success || result.Value is not "foo")
        {
            throw new Exception($"Unexpected result: {result.Value}");
        }
    }

    public static void ShouldRejectPromise()
    {
        var promise = Promise.Reject<string>("nope");
        var result = Promise.GetAwaitResult(promise);
        if (result.Success)
        {
            throw new Exception("Expected rejection to fail the promise.");
        }

        if (!Promise.Error.Is(result.Error))
        {
            throw new Exception("Expected rejection reason to be a Promise.Error");
        }

        if (!Promise.Error.IsKind(result.Error, Promise.Error.Kind.ExecutionError))
        {
            throw new Exception($"Unexpected error kind: {Promise.Error.GetKind(result.Error)}");
        }

        if (Promise.Error.GetMessage(result.Error) != "nope")
        {
            throw new Exception($"Unexpected error message: {Promise.Error.GetMessage(result.Error)}");
        }
    }

    public static void ShouldChainPromises()
    {
        var promise = Promise.Resolve<int>(1)
            .Then(value => value + 1)
            .Then(value => Promise.Resolve<int>(value * 2));

        var result = Promise.GetAwaitResult(promise);
        if (!result.Success || result.Value is not 4)
        {
            throw new Exception($"Unexpected result: {result.Value}");
        }
    }

    public static void ShouldPreserveThenOrdering()
    {
        var log = new List<int>();
        var chained = Promise.Resolve(1)
            .Then(first =>
            {
                log.Add(first);
                return Promise.Delay(0.01, first + 1);
            })
            .Then(second =>
            {
                log.Add(second);
                return second + 1;
            });

        var result = Promise.GetAwaitResult(chained);
        if (!result.Success || result.Value is not 3)
        {
            throw new Exception($"Promise chain produced unexpected result: {result.Value}");
        }

        if (log.Count != 2 || log[0] != 1 || log[1] != 2)
        {
            throw new Exception($"Promise chain recorded unexpected ordering: [{string.Join(", ", log)}]");
        }
    }

    public static void ShouldCancelPromise()
    {
        if (Promise.Error.Kind.AlreadyCancelled != "AlreadyCancelled")
        {
            throw new Exception($"Unexpected alias for AlreadyCancelled: {Promise.Error.Kind.AlreadyCancelled}");
        }

        var promise = Promise.Delay(0.05);
        Promise.Cancel(promise, "cancelled");

        var result = Promise.GetAwaitResult(promise);
        if (result.Success)
        {
            throw new Exception("Expected cancellation to fail the promise.");
        }

        if (!Promise.Error.Is(result.Error))
        {
            throw new Exception("Expected cancellation reason to be a Promise.Error");
        }

        var cancellationKind = Promise.Error.GetKind(result.Error);
        if (cancellationKind != Promise.Error.Kind.AlreadyCancelled && cancellationKind != Promise.Error.Kind.ExecutionError)
        {
            throw new Exception($"Unexpected cancellation kind: {cancellationKind}");
        }

        if (Promise.Error.GetMessage(result.Error) != "cancelled")
        {
            throw new Exception($"Unexpected cancellation message: {Promise.Error.GetMessage(result.Error)}");
        }
    }

    public static void ShouldTimeoutPromise()
    {
        var slow = Promise.Delay(0.5);
        var timed = Promise.Timeout(slow, 0.01);

        var result = Promise.GetAwaitResult(timed);
        if (result.Success)
        {
            throw new Exception("Expected timeout to fail the promise.");
        }

        if (!Promise.Error.Is(result.Error))
        {
            throw new Exception("Expected timeout to produce a Promise.Error");
        }

        if (!Promise.Error.IsKind(result.Error, Promise.Error.Kind.TimedOut))
        {
            throw new Exception($"Unexpected timeout kind: {Promise.Error.GetKind(result.Error)}");
        }
    }

    public static void ShouldTryPromise()
    {
        var success = Promise.GetAwaitResult(Promise.Try(() => "value"));
        if (!success.Success || success.Value is not "value")
        {
            throw new Exception($"Promise.Try did not resolve as expected: {success.Value}");
        }

        var failure = Promise.GetAwaitResult(Promise.Try<string>(() =>
        {
            throw new Exception("boom");
#pragma warning disable CS0162
            return string.Empty;
#pragma warning restore CS0162
        }));
        if (failure.Success || !Promise.Error.IsKind(failure.Error, Promise.Error.Kind.ExecutionError))
        {
            throw new Exception("Promise.Try should reject when the callback throws.");
        }
    }

    public static void ShouldRetryPromise()
    {
        var attempts = 0;
        var success = Promise.GetAwaitResult(Promise.Retry(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                return Promise.Reject<string>("fail");
            }

            return Promise.Resolve("ok");
        }, 3));

        if (!success.Success || success.Value is not "ok")
        {
            throw new Exception($"Promise.Retry did not resolve as expected: {success.Value}");
        }

        if (attempts != 3)
        {
            throw new Exception($"Promise.Retry should attempt 3 times, got {attempts} attempts.");
        }

        var failure = Promise.GetAwaitResult(Promise.Retry(() => Promise.Reject<string>("nope"), 1));
        if (failure.Success)
        {
            throw new Exception("Promise.Retry should reject when retries are exhausted.");
        }
    }

    public static void ShouldRetryPromiseWithSynchronousFailures()
    {
        var attempts = 0;
        var success = Promise.GetAwaitResult(Promise.Retry(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new Exception($"sync-failure-{attempts}");
            }

            return Promise.Resolve("ok-sync");
        }, 2));

        if (!success.Success || success.Value is not "ok-sync")
        {
            throw new Exception($"Promise.Retry should resolve after synchronous retries: {success.Value}");
        }

        if (attempts != 3)
        {
            throw new Exception($"Promise.Retry should attempt 3 times with synchronous failures, got {attempts} attempts.");
        }
    }

    public static void ShouldRetryWithDelay()
    {
        var attempts = 0;
        var success = Promise.GetAwaitResult(Promise.RetryWithDelay(() =>
        {
            attempts++;
            if (attempts < 2)
            {
                return Promise.Reject<string>("again");
            }

            return Promise.Resolve("delayed");
        }, 2, 0.01));

        if (!success.Success || success.Value is not "delayed")
        {
            throw new Exception($"Promise.RetryWithDelay did not resolve as expected: {success.Value}");
        }

        if (attempts != 2)
        {
            throw new Exception($"Promise.RetryWithDelay should attempt twice, got {attempts}.");
        }
    }

    public static void ShouldRetryWithDelayHandleSynchronousFailures()
    {
        var attempts = 0;
        var success = Promise.GetAwaitResult(Promise.RetryWithDelay(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new Exception($"sync-delay-failure-{attempts}");
            }

            return Promise.Resolve("sync-delay");
        }, 2, 0.01));

        if (!success.Success || success.Value is not "sync-delay")
        {
            throw new Exception($"Promise.RetryWithDelay should resolve after synchronous retries: {success.Value}");
        }

        if (attempts != 3)
        {
            throw new Exception($"Promise.RetryWithDelay should attempt 3 times with synchronous failures, got {attempts} attempts.");
        }
    }

    public static void ShouldAggregatePromiseAnyRejections()
    {
        var rejection = Promise.Any<string>(
            Promise.Reject<string>("alpha"),
            Promise.Reject<string>("beta")
        );

        var result = Promise.GetAwaitResult(rejection);
        if (result.Success)
        {
            throw new Exception("Promise.Any should reject when all promises reject.");
        }

        if (result.Error is null)
        {
            throw new Exception("Promise.Any should surface an aggregated error payload.");
        }

        if (!Promise.Error.Is(result.Error))
        {
            throw new Exception("Promise.Any aggregated error should be wrapped as a Promise.Error.");
        }

        var message = Promise.Error.GetMessage(result.Error);
        if (message != "beta")
        {
            throw new Exception($"Promise.Any should propagate the last rejection message, got: {message}");
        }
    }

    public static void ShouldReturnAllSettledResults()
    {
        var settled = Promise.AllSettled(
            Promise.Resolve("ok"),
            Promise.Reject<string>("nope")
        );

        var result = Promise.GetAwaitResult(settled);
        if (!result.Success)
        {
            throw new Exception("Promise.AllSettled should always resolve.");
        }

        if (result.Value is null)
        {
            throw new Exception("Promise.AllSettled should resolve with aggregated results.");
        }

        if (Promise.Error.Is(result.Value))
        {
            throw new Exception("Promise.AllSettled should resolve with aggregated entries, not a Promise.Error.");
        }
    }

    public static void ShouldResolveFromEvent()
    {
        var signal = new PromiseTestSignal();
        var promise = Promise.FromEvent<string>(signal, value => value == "fire");

        signal.Fire("noop");
        signal.Fire("fire");

        var result = Promise.GetAwaitResult(promise);
        if (!result.Success)
        {
            throw new Exception($"Promise.FromEvent should resolve but failed with {Promise.Error.GetMessage(result.Error)}");
        }

        if (result.Value?.ToString() != "fire")
        {
            throw new Exception($"Promise.FromEvent resolved with unexpected value: {result.Value}");
        }

        if (signal.GetListenerCount() != 0)
        {
            throw new Exception("Promise.FromEvent should disconnect listeners after resolving.");
        }
    }

    public static void ShouldResolveFromEventWithBufferedFire()
    {
        var signal = new PromiseTestSignal();
        signal.Queue("noop");
        signal.Queue("fire");

        var promise = Promise.FromEvent<string>(signal, value => value == "fire");
        var result = Promise.GetAwaitResult(promise);
        if (!result.Success)
        {
            throw new Exception($"Promise.FromEvent should resolve buffered events but failed with {Promise.Error.GetMessage(result.Error)}");
        }

        if (result.Value?.ToString() != "fire")
        {
            throw new Exception($"Promise.FromEvent buffered event resolved with unexpected value: {result.Value}");
        }

        if (signal.GetListenerCount() != 0)
        {
            throw new Exception("Promise.FromEvent should disconnect listeners after handling buffered events.");
        }
    }

    public static void ShouldCascadeCancellationThroughTimeout()
    {
        var attempts = 0;
        var retrying = Promise.Retry(() =>
        {
            attempts++;
            return Promise.Delay(0.05, attempts);
        }, 5);

        var timedOut = Promise.Timeout(retrying, 0.01);
        var result = Promise.GetAwaitResult(timedOut);
        if (result.Success)
        {
            throw new Exception("Promise.Timeout should cancel the retrying promise and reject.");
        }

        if (!Promise.Error.IsKind(result.Error, Promise.Error.Kind.TimedOut))
        {
            throw new Exception($"Promise.Timeout should reject with TimedOut, got: {Promise.Error.GetKind(result.Error)}");
        }

        if (attempts != 1)
        {
            throw new Exception($"Promise retry should stop after cancellation, attempts recorded: {attempts}");
        }
    }

}

internal static class AsyncStaticExample
{
    public static async Task<string> Foo() => "foo";

    public static async Task<string> Bar() => (await Foo()) + "bar";
}

internal sealed class AsyncInstanceExample
{
    public async Task<string> Foo() => "foo";

    public async Task<string> Bar() => (await Foo()) + "bar";
}
