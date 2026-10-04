namespace RoslynReview.Core.Workspace;

/// <summary>Which solution to analyze and how to interpret paths in diffs.</summary>
/// <param name="SolutionPath">Path to a .sln, .slnx or .csproj file.</param>
/// <param name="RepositoryRoot">
/// Directory that diff paths are relative to. When <see langword="null"/>, the nearest ancestor
/// of the solution that contains a <c>.git</c> entry is used, falling back to the solution's directory.
/// </param>
public sealed record WorkspaceOptions(string SolutionPath, string? RepositoryRoot = null);
