namespace ChangeLens.Application.Exceptions;

/// <summary>Resource (or the project it belongs to) does not exist or is not visible to the caller (404).</summary>
public sealed class NotFoundException : ChangeLensException
{
    public NotFoundException(string message)
        : base(404, "not_found", message)
    {
    }
}
