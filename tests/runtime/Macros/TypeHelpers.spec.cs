using System;

namespace RuntimeSpecs.Macros;

public static class TypeHelpersSpec
{
    public static void ShouldMatchPrimitiveTypes()
    {
        if (!typeIs("hello", "string"))
        {
            throw new Exception("typeIs(\"hello\", \"string\") should return true.");
        }

        if (typeIs("hello", "number"))
        {
            throw new Exception("typeIs should return false for mismatched primitive types.");
        }
    }

    public static void ShouldMatchClassNames()
    {
        var widget = new FakeWidget();
        if (!classIs(widget, "FakeWidget"))
        {
            throw new Exception("classIs should compare the ClassName field.");
        }

        if (classIs(widget, "Other"))
        {
            throw new Exception("classIs should return false for different class names.");
        }
    }

    private sealed class FakeWidget
    {
        public string ClassName { get; } = "FakeWidget";
    }
}
