namespace RobloxCS.AST.Types;

public sealed class OptionalTypeInfo : TypeInfo {
    public required TypeInfo Inner { get; set; }

    public override OptionalTypeInfo DeepClone() => new() { Inner = (TypeInfo)Inner.DeepClone() };
    public override void Accept(IAstVisitor v) => v.VisitOptionalTypeInfo(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitOptionalTypeInfo(this);

    public override IEnumerable<AstNode> Children() {
        yield return Inner;
    }
}
