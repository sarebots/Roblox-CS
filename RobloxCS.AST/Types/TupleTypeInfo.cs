using System.Linq;

namespace RobloxCS.AST.Types;

public sealed class TupleTypeInfo : TypeInfo {
    public required List<TypeInfo> Elements { get; set; }

    /// <summary>
    /// Optional variadic tail (e.g. <c>...T</c>) appended to the tuple.
    /// </summary>
    public VariadicTypeInfo? VariadicTail { get; set; }

    public override TupleTypeInfo DeepClone() => new() {
        Elements = Elements.Select(element => (TypeInfo)element.DeepClone()).ToList(),
        VariadicTail = (VariadicTypeInfo?)VariadicTail?.DeepClone(),
    };

    public override void Accept(IAstVisitor v) => v.VisitTupleTypeInfo(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitTupleTypeInfo(this);

    public override IEnumerable<AstNode> Children() {
        foreach (var element in Elements) yield return element;
        if (VariadicTail is not null) yield return VariadicTail;
    }
}
