using Roblox;

class MacroPromiseFromEventPredicate
{
    public void Watch(Signal signal)
    {
        Promise.FromEvent(signal.Event, value => value); // expect: [ROBLOXCS3016] Promise.FromEvent predicates must return a boolean expression.
    }
}
