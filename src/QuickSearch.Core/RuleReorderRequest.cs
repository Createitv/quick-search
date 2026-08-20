namespace QuickSearch.Core;

public sealed record RuleReorderRequest(
    Guid RuleId,
    Guid TargetRuleId,
    bool PlaceAfterTarget);
