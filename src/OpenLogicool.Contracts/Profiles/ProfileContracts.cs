namespace OpenLogicool.Contracts.Profiles;

public sealed record ApplicationIdentity(
    string SchemaVersion,
    string ApplicationId,
    string FullPath,
    string? PackageIdentity,
    long ProcessGeneration,
    string? WindowMatcher);

public sealed record BindingRevision(
    string SchemaVersion,
    string ApplicationId,
    string DeviceInstanceId,
    string LayerId,
    string MappingRevision,
    IReadOnlyList<string> Outputs);
