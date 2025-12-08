using System.Collections.Generic;

class TuplePatternGuardRestCountIsPattern
{
    public int Describe((List<int> Values, int Tail) input) => input switch
    {
        ([var head, .. var rest], var tail) when rest.Count is > var threshold && threshold > head => tail,
        (_,[.. var rest]) when rest.Count is > var secondThreshold && secondThreshold > 10 => 0,
        _ => 0,
    };
}
