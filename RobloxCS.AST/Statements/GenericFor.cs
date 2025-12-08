using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Statements;

public sealed class GenericFor : Statement {
    public required List<Var> Names { get; set; }
    public required List<Expression> Expressions { get; set; }
    public required Block Body { get; set; }

    public override void Accept(IAstVisitor visitor) => visitor.VisitGenericFor(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitGenericFor(this);

    public override AstNode DeepClone() {
        return new GenericFor {
            Names = Names.Select(name => (Var)name.DeepClone()).ToList(),
            Expressions = Expressions.Select(expr => (Expression)expr.DeepClone()).ToList(),
            Body = Body.DeepClone(),
        };
    }

    public override IEnumerable<AstNode> Children() {
        foreach (var name in Names) {
            yield return name;
        }

        foreach (var expr in Expressions) {
            yield return expr;
        }

        yield return Body;
    }
}
