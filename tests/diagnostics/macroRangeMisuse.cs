using System.Collections.Generic;

class MacroRangeMisuse
{
    public IEnumerable<int> Collect()
    {
        var values = range(0, 10); // expect: [ROBLOXCS3014] range can only be used inside foreach iteration expressions.
        return new[] { values.GetHashCode() };
    }
}
