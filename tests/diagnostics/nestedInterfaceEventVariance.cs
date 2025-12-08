using System;

public interface IEventBase
{
    event Action Fired;
}

public class EventContainer
{
    public interface Derived : IEventBase
    {
        event Action<int> Fired; // expect: Nested interface member 'Fired' conflicts with inherited member signature.
    }
}
