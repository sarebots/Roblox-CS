using System.Collections.Generic;

namespace RuntimeSpecs.Collections;

public static class ListSliceSpec
{
    public static void SliceCaptureShouldReturnMiddle()
    {
        var values = new[] { 1, 2, 3, 4, 5 };
        var captured = Capture(values);
        if (captured.Count != 2 || captured[0] != 2 || captured[1] != 3)
        {
            throw new System.Exception($"Expected [2, 3] slice, got [{string.Join(", ", captured)}]");
        }
    }

    public static void SliceCaptureShouldReturnEmptyForExactMatch()
    {
        var values = new[] { 10, 20 };
        var captured = Capture(values);
        if (captured.Count != 0)
        {
            throw new System.Exception("Expected empty slice when only head/tail remain.");
        }
    }

    private static List<int> Capture(int[] values)
    {
        switch (values)
        {
            case [1, .. var middle, 4, 5]:
                return new List<int>(middle);
            default:
                return new List<int>();
        }
    }
}
