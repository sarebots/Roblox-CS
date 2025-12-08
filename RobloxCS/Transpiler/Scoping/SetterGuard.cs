using RobloxCS.Shared;

namespace RobloxCS.TranspilerV2.Scoping;

public sealed class SetterGuard<T> : IDisposable {
    private readonly Action<T> _setter;
    private readonly T _prev;

    public SetterGuard(Action<T> setter, T prev, T next) {
        _setter = setter;
        _prev = prev;

        _setter(next);
        
        Logger.Debug($"Pushed setter guard prev={prev} next={next}");
    }

    public void Dispose() {
        _setter(_prev);
        
        Logger.Debug("Popped setter guard");
    }
}
