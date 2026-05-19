namespace Tracer.Cli;

/// <summary>Опции запуска приложения</summary>
internal readonly record struct MainOptions(string? Host, int MaxTtl, int TimeoutMs, int ProbesPerHop, bool NoDns, bool Json, bool ShowVersion, bool RunUpdate, string? ErrorMessage);
