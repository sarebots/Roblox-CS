using Roblox;

public class MockConnection
{
    public void Disconnect()
    {
    }
}

public class MockEvent
{
    public MockConnection Connect(System.Action<object> callback)
    {
        return new MockConnection();
    }
}

public static class PromiseFromEventPredicateDiagnostic
{
    public static void Run()
    {
        var mockEvent = new MockEvent();
        Promise.FromEvent(mockEvent, value => value); // expect: [ROBLOXCS3016] Promise.FromEvent predicates must return a boolean expression.
    }
}
