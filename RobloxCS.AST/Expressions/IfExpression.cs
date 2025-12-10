using System;
using System.Collections.Generic;

namespace RobloxCS.AST.Expressions;

public class IfExpression : Expression
{
    public required Expression Condition { get; set; }
    public required Expression TrueValue { get; set; }
    public required Expression FalseValue { get; set; }

    public override void Accept(IAstVisitor visitor) => visitor.VisitIfExpression(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitIfExpression(this);

    public override AstNode DeepClone() => new IfExpression
    {
        Condition = (Expression)Condition.DeepClone(),
        TrueValue = (Expression)TrueValue.DeepClone(),
        FalseValue = (Expression)FalseValue.DeepClone(),
    };

    public override IEnumerable<AstNode> Children()
    {
        yield return Condition;
        yield return TrueValue;
        yield return FalseValue;
    }

    public override string ToString()
    {
        return $"if {Condition} then {TrueValue} else {FalseValue}";
    }
}

