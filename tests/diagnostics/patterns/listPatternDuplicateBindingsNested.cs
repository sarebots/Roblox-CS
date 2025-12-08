using System.Collections.Generic;

class ListPatternDuplicateBindingsNested
{
    public string Describe(List<Node> nodes) =>
        nodes switch
        {
            [{ Child: { Label: var label }, Label: var label }, .. _] => label, // expect: Variable 'label' is bound multiple times in the same list pattern.
            _ => "none",
        };

    private sealed class Node
    {
        public string Label { get; set; } = "";
        public Node? Child { get; set; }
    }
}
