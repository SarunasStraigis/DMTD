namespace Dmtd.Core;

public readonly record struct HistoryQuery(string? Since = null, string? Until = null);
