using System.Collections.Generic;

class TuplePatternIsAndRestLength
{
    public int Describe(List<int> values)
    {
        if (values is [var head, .. var rest, var tail] && rest.Length > head) // expect: [ROBLOXCS2013] Guard expressions cannot reference slice counts before bindings are established.
        {
            return tail;
        }

        return 0;
    }
}
