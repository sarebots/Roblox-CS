using System;

namespace Runtime.String;

public static class Gmatch
{
    public static void IterateCharacters()
    {
        var total = 0;

        foreach (var ch in "abcd")
        {
            total += 1;
        }

        if (total != 4)
        {
            throw new Exception($"Expected 4 characters, got {total}");
        }
    }
}
