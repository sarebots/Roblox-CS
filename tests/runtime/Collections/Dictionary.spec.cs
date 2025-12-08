using System.Collections.Generic;
using Roblox;

namespace RuntimeSpecs.Collections;

public static class DictionarySpec
{
    public static void ShouldInitialiseDictionaryLiteral()
    {
        var dictionary = new Dictionary<string, int>
        {
            ["foo"] = 1,
            ["bar"] = 2,
        };

        var result = Promise.GetAwaitResult(Promise.Resolve(dictionary));
        if (!result.Success)
        {
            throw new System.Exception("Dictionary literal should resolve successfully.");
        }

        if (result.Value is not Dictionary<string, int> actual)
        {
            throw new System.Exception("Expected dictionary literal to resolve to Dictionary<string, int>.");
        }

        if (actual["foo"] != 1 || actual["bar"] != 2)
        {
            throw new System.Exception($"Dictionary literal produced unexpected values: foo={actual["foo"]}, bar={actual["bar"]}");
        }
    }

    public static void ShouldInitialiseDictionaryWithCollectionExpression()
    {
        var dictionary = new Dictionary<string, int>
        {
            { "alpha", 10 },
            { "beta", 20 },
        };

        if (dictionary.Count != 2 || dictionary["alpha"] != 10 || dictionary["beta"] != 20)
        {
            throw new System.Exception("Dictionary collection expression produced unexpected values.");
        }
    }
}
