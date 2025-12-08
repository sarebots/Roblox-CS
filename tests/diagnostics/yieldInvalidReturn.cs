using System.Collections.Generic;

class YieldInvalidReturn
{
    public int Values()
    {
        yield return 1; // expect: yield statements are only supported in iterator methods that return IEnumerable<T> or IEnumerator<T>.
        return 0;
    }
}
