using System;
using System.Collections.Generic;

namespace RuntimeSpecs.Using;

public static class NestedUsingSpec
{
    public static void ShouldDisposeInnerBeforeOuter()
    {
        var log = new List<string>();

        using (var outer = new TrackingDisposable("outer", log))
        {
            using (var inner = new TrackingDisposable("inner", log))
            {
            }
        }

        if (log.Count != 2)
        {
            throw new Exception($"Expected two dispose calls, received {log.Count}.");
        }

        if (log[0] != "inner" || log[1] != "outer")
        {
            throw new Exception($"Dispose order incorrect: [{string.Join(", ", log)}]");
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TrackingDisposable(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Dispose()
        {
            _log.Add(_name);
        }
    }
}
