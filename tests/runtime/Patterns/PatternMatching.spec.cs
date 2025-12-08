using System;
using System.Collections.Generic;

namespace RuntimeSpecs.Patterns;

public static class PatternMatchingSpec
{
    public static void TuplePatternShouldCaptureAndGuard()
    {
        var tuple = (First: 2, Second: 3);
        var result = DescribeTuple(tuple);
        if (result != "2:3")
        {
            throw new Exception($"Expected guard match for (2, 3), received '{result}'.");
        }

        tuple = (-1, 5);
        result = DescribeTuple(tuple);
        if (result != "other")
        {
            throw new Exception($"Expected fallback branch for (-1, 5), received '{result}'.");
        }

        tuple = (5, 4);
        result = DescribeTuple(tuple);
        if (result != "descending:9")
        {
            throw new Exception($"Expected positional guard fallback 'descending:9', received '{result}'.");
        }
    }

    public static void ListPatternShouldCaptureHeadTupleAndSlice()
    {
        var entries = new List<(string Label, int Score)>
        {
            ("alpha", 15),
            ("beta", 5),
            ("gamma", 1),
        };

        var result = DescribeEntries(entries);
        if (result != "alpha:2")
        {
            throw new Exception($"Expected slice length message 'alpha:2', received '{result}'.");
        }
    }

    public static void ListPatternShouldBindBeforeGuard()
    {
        var values = new List<int> { 8, 9, 10 };
        var result = DescribeHeadGuard(values);
        if (result != 8 + 2)
        {
            throw new Exception($"Expected guard branch result 10, received '{result}'.");
        }

        values = new List<int> { 1, 2, 3 };
        result = DescribeHeadGuard(values);
        if (result != 0)
        {
            throw new Exception($"Expected fallback result 0, received '{result}'.");
        }
    }

    public static void ListPatternShouldHandleDiscards()
    {
        var values = new List<int> { 0, 5, 6, 7 };
        var result = DescribeDiscard(values);
        if (result != "rest:2")
        {
            throw new Exception($"Expected discard pattern to capture rest length 'rest:2', received '{result}'.");
        }

        values = new List<int> { 1, 2, 3 };
        result = DescribeDiscard(values);
        if (result != "none")
        {
            throw new Exception($"Expected discard fallback 'none', received '{result}'.");
        }
    }

    public static void ListPatternShouldCompareRestAgainstTail()
    {
        var values = new List<int> { 3, 1, 2, 4 };
        var result = DescribeRestComparison(values);
        if (result != "match:2")
        {
            throw new Exception($"Expected guard match with rest length 2, received '{result}'.");
        }

        values = new List<int> { 3, 1, 2 };
        result = DescribeRestComparison(values);
        if (result != "none")
        {
            throw new Exception($"Expected guard fallback 'none', received '{result}'.");
        }
    }

    public static void ListPatternShouldCompareMultipleRests()
    {
        var values = new List<int> { 1, 2, 3, 4, 5 };
        var result = DescribeMultiRest(values);
        if (result != "outer:2:1")
        {
            throw new Exception($"Expected multi-rest match 'outer:2:1', received '{result}'.");
        }

        values = new List<int> { 1, 2, 3 };
        result = DescribeMultiRest(values);
        if (result != "none")
        {
            throw new Exception($"Expected multi-rest fallback 'none', received '{result}'.");
        }
    }

    public static void ListPatternShouldGuardUsingRestLength()
    {
        var values = new List<int> { 1, 4, 5 };
        var result = DescribeRestLengthGuard(values);
        if (result != "rest:2")
        {
            throw new Exception($"Expected rest-length guard match 'rest:2', received '{result}'.");
        }

        values = new List<int> { 6, 7 };
        result = DescribeRestLengthGuard(values);
        if (result != "none")
        {
            throw new Exception($"Expected rest-length guard fallback 'none', received '{result}'.");
        }
    }

    public static void ListPatternShouldHandleSingleDiscardWithSlice()
    {
        var values = new List<int> { 0, 1, 2, 3 };
        var result = DescribeSingleDiscardSlice(values);
        if (result != "middle:1:3")
        {
            throw new Exception($"Expected single-discard slice result 'middle:1:3', received '{result}'.");
        }

        values = new List<int> { 1, 2 };
        result = DescribeSingleDiscardSlice(values);
        if (result != "none")
        {
            throw new Exception($"Expected single-discard slice fallback 'none', received '{result}'.");
        }
    }

    public static void RecursivePatternShouldBindNestedProperties()
    {
        var root = new Node
        {
            Label = "root",
            Value = 10,
            Child = new Node
            {
                Label = "mid",
                Name = "child",
                Value = 25,
                Child = new Node { Label = "leaf", Value = 1 },
            },
        };

        var result = DescribeNode(root);
        if (result != "child:25")
        {
            throw new Exception($"Expected nested binding 'child:25', received '{result}'.");
        }

        var fallback = new Node { Label = "fallback", Value = 5 };
        result = DescribeNode(fallback);
        if (result != "value:5")
        {
            throw new Exception($"Expected fallback value binding, received '{result}'.");
        }

        var nestedChain = new Node
        {
            Label = "zero",
            Value = 0,
            Child = new Node
            {
                Label = "first",
                Value = 10,
                Child = new Node
                {
                    Label = "second",
                    Value = 20,
                },
            },
        };

        result = DescribeNode(nestedChain);
        if (result != "chain:10:20")
        {
            throw new Exception($"Expected nested chain binding 'chain:10:20', received '{result}'.");
        }
    }

    public static void ListPatternShouldHandleNestedTupleNodes()
    {
        var nodes = new List<Node>
        {
            new Node
            {
                Label = "outer",
                Value = 5,
                Child = new Node
                {
                    Label = "inner",
                    Value = 10,
                },
            },
        };

        var result = DescribeNestedList(nodes);
        if (result != "outer:inner")
        {
            throw new Exception($"Expected nested tuple list match 'outer:inner', received '{result}'.");
        }

        nodes = new List<Node>
        {
            new Node { Label = "partial", Value = 1 },
            new Node { Label = "extra", Value = 2 },
        };

        result = DescribeNestedList(nodes);
        if (result != "none")
        {
            throw new Exception($"Expected nested tuple fallback 'none', received '{result}'.");
        }
    }

    private static string DescribeTuple((int First, int Second) tuple) =>
        tuple switch
        {
            (var first, var second) when first > 0 => $"{first}:{second}",
            (var first, var second) when first > second => $"descending:{first + second}",
            _ => "other",
        };

    private static string DescribeEntries(List<(string Label, int Score)> entries) =>
        entries switch
        {
            [(var label, var score), .. var rest] when score > 10 => $"{label}:{rest.Count}",
            _ => "none",
        };

    private static int DescribeHeadGuard(List<int> values) =>
        values switch
        {
            [var head, .. var rest] when head > 5 => head + rest.Count,
            _ => 0,
        };

    private static string DescribeDiscard(List<int> values) =>
        values switch
        {
            [_, 5, .. var rest, _] => $"rest:{rest.Count}",
            _ => "none",
        };

    private static string DescribeRestComparison(List<int> values) =>
        values switch
        {
            [var head, .. var rest, var tail] when tail > rest.Count => $"match:{rest.Count}",
            _ => "none",
        };

    private static string DescribeMultiRest(List<int> values) =>
        values switch
        {
            [var start, .. var rest, var tail] when rest.Count >= 2 && tail == 5 => $"outer:{rest.Count}:{tail - rest[^1]}",
            _ => "none",
        };

    private static string DescribeRestLengthGuard(List<int> values) =>
        values switch
        {
            [var head, .. var rest] when rest.Count >= 2 && head < rest.Count => $"rest:{rest.Count}",
            _ => "none",
        };

    private static string DescribeSingleDiscardSlice(List<int> values) =>
        values switch
        {
            [_, .. var middle] when middle.Count >= 2 => $"middle:{middle[0]}:{middle[^1]}",
            [.. var middle, _] when middle.Count >= 2 => $"middle:{middle[0]}:{middle[^1]}",
            _ => "none",
        };

    public static void ListPatternShouldRejectConflictingBindings()
    {
        var values = new List<int> { 1, 2, 3 };
        try
        {
            _ = values switch
            {
                [var head, var head, .. var rest] => $"dup:{rest.Count}",
                _ => "none",
            };
            throw new Exception("Expected conflicting binding to throw.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string DescribeNode(Node node) =>
        node switch
        {
            { Value: 0, Child: { Value: var childValue, Child: { Value: var grand } } } => $"chain:{childValue}:{grand}",
            { Child: { Name: var name, Value: var childValue } } when childValue > node.Value => $"{name}:{childValue}",
            { Value: var value } => $"value:{value}",
            _ => "none",
        };

    private static string DescribeNestedList(List<Node> nodes) =>
        nodes switch
        {
            [{ Label: var label, Child: { Label: var childLabel } }, .. var rest] when rest.Count == 0 => $"{label}:{childLabel}",
            _ => "none",
        };

    private sealed class Node
    {
        public string Label { get; set; } = "";
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public Node? Child { get; set; }
    }
}
