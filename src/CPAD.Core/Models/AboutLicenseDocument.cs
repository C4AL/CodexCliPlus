namespace CPAD.Core.Models;

public sealed record AboutLicenseDocument(
    string Name,
    string License,
    string OutputFileName,
    string RepositoryRelativePath,
    string Summary);
