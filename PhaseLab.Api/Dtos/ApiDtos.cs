namespace PhaseLab.Api;

public sealed class AppInfoDto
{
    public required string Name { get; init; }
    public required string ApiVersion { get; init; }
    public required IReadOnlyList<string> ModuleIds { get; init; }
}

public sealed class ModuleInfoDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public required IReadOnlyList<string> Actions { get; init; }
}

public sealed class ModuleStatusDto
{
    public required string ModuleId { get; init; }
    public required bool Capturing { get; init; }
    public required string StatusText { get; init; }
    public string? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public int? SampleRate { get; init; }
}

public sealed class ModuleSnapshotDto
{
    public required string ModuleId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool Capturing { get; init; }
    public required string StatusText { get; init; }
    public object? Data { get; init; }
}

public sealed class AudioDeviceDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class CaptureRequestDto
{
    public string? DeviceId { get; init; }
    public int? SampleRate { get; init; }
    public int? InputChannel { get; init; }
}

public sealed class ActionResultDto
{
    public required string Status { get; init; }
    public string? Message { get; init; }
}
