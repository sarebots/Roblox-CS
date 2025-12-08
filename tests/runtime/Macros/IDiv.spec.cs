using System;

namespace RuntimeSpecs.Macros;

public static class IDivSpec
{
    public static void ShouldIntegerDividePositiveValues()
    {
        var result = 10.idiv(3);
        if (result != 3)
        {
            throw new Exception($"Expected 10 idiv 3 to equal 3, got {result}");
        }
    }

    public static void ShouldIntegerDivideNegativeValues()
    {
        var result = (-7).idiv(2);
        if (result != -4)
        {
            throw new Exception($"Expected -7 idiv 2 to equal -4, got {result}");
        }
    }
}
