using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Types;

public sealed class TypeOfTypeInfo : TypeInfo {
    public required Expression Expression { get; set; }

    public override TypeOfTypeInfo DeepClone() => new() { Expression = (Expression)Expression.DeepClone() };
    public override void Accept(IAstVisitor v) => v.VisitTypeOfTypeInfo(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitTypeOfTypeInfo(this);

    public override IEnumerable<AstNode> Children() {
        yield return Expression;
    }
}
