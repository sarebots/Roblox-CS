using System;
using System.Threading.Tasks;

namespace Roblox;

public readonly record struct PromiseAwaitResult<T>(bool Success, T? Value, object? Error);

public sealed class Promise<T>
{
    public Promise<TNext> Then<TNext>(Func<T, TNext> onFulfilled) => throw new NotImplementedException();

    public Promise<TNext> Then<TNext>(Func<T, Promise<TNext>> onFulfilled) => throw new NotImplementedException();

    public Promise<T> Catch(Func<object?, T> onRejected) => throw new NotImplementedException();

    public Promise<T> Catch(Func<object?, Promise<T>> onRejected) => throw new NotImplementedException();

    public Promise<T> Finally(Action onFinally) => throw new NotImplementedException();

    public PromiseAwaitResult<T> Await() => throw new NotImplementedException();

    public void Cancel() => throw new NotImplementedException();
}

public static class Promise
{
    public static Promise<T> Resolve<T>(T value) => throw new NotImplementedException();

    public static Promise<T> Reject<T>(object? reason) => throw new NotImplementedException();

    public static Promise<T[]> All<T>(params Promise<T>[] promises) => throw new NotImplementedException();

    public static Promise<T> Delay<T>(double seconds, T value) => throw new NotImplementedException();

    public static Promise<object?> Delay(double seconds) => throw new NotImplementedException();

    public static Promise<T> Timeout<T>(Promise<T> promise, double seconds, object? reason = null) => throw new NotImplementedException();

    public static Promise<T> Retry<T>(Func<Promise<T>> factory, int retries = 1) => throw new NotImplementedException();

    public static Promise<T> RetryWithDelay<T>(Func<Promise<T>> factory, int retries, double seconds) => throw new NotImplementedException();

    public static Promise<T> Try<T>(Func<T> callback) => throw new NotImplementedException();

    public static Promise<T> Try<T>(Func<Promise<T>> callback) => throw new NotImplementedException();

    public static Promise<T> FromEvent<T>(object eventSource, Func<T, bool>? predicate = null) => throw new NotImplementedException();

    public static Promise<T> Any<T>(params Promise<T>[] promises) => throw new NotImplementedException();

    public static Promise<object?> AllSettled<T>(params Promise<T>[] promises) => throw new NotImplementedException();

    public static Func<TResult> Async<TResult>(Func<TResult> callback) => throw new NotImplementedException();

    public static Func<Promise<TResult>> Async<TResult>(Func<Promise<TResult>> callback) => throw new NotImplementedException();

    public static void Cancel(Task promise, object? reason = null) => throw new NotImplementedException();

    public static void Cancel<T>(Promise<T> promise, object? reason = null) => throw new NotImplementedException();

    public static PromiseAwaitResult<T> GetAwaitResult<T>(Promise<T> promise) => throw new NotImplementedException();

    public static PromiseAwaitResult<T> GetAwaitResult<T>(Task<T> task) => throw new NotImplementedException();

    public static PromiseAwaitResult<object?> GetAwaitResult(Task task) => throw new NotImplementedException();

    public static TResult Await<TResult>(Promise<TResult> promise) => throw new NotImplementedException();

    public static TResult Await<TResult>(TResult value) => value;

    public static class Error
    {
        public static bool Is(object? value) => throw new NotImplementedException();

        public static bool IsKind(object? value, string kind) => throw new NotImplementedException();

        public static string GetKind(object? value) => throw new NotImplementedException();

        public static string GetMessage(object? value) => throw new NotImplementedException();

        public static class Kind
        {
            public const string ExecutionError = "ExecutionError";
            public const string AlreadyCancelled = "AlreadyCancelled";
            public const string TimedOut = "TimedOut";
            public const string NotResolvedInTime = "NotResolvedInTime";
        }
    }
}
