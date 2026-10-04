namespace RoslynReview.Core.Workspace;

/// <summary>The solution could not be opened. The message is safe to show to the MCP client.</summary>
public sealed class SolutionLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
