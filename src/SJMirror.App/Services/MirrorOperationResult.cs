namespace SJMirror.App.Services;

public sealed record MirrorOperationResult(bool IsSuccess, string? ErrorMessage)
{
    public static MirrorOperationResult Success { get; } = new(true, null);

    public static MirrorOperationResult Failure(string errorMessage) => new(false, errorMessage);
}
