namespace Aurora.Content
{
    internal sealed record CharacterWarningResult(
        string WarningKind,
        string Severity,
        string Message,
        string OwnerName,
        string OwnerTypeName,
        string SelectName);
}
