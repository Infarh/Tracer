namespace Tracer.Cli;

/// <summary>Опции запуска приложения</summary>
internal readonly record struct MainOptions(string? Host, int MaxTtl, int TimeoutMs, int ProbesPerHop, bool NoDns, bool Json, string? ErrorMessage)
{
    /// <summary>Разбирает аргументы командной строки</summary>
    /// <param name="Args">Входные аргументы</param>
    /// <returns>Набор разобранных опций или сообщение об ошибке</returns>
    public static MainOptions Parse(string[] Args)
    {
        ArgumentNullException.ThrowIfNull(Args);

        string? host = null;
        var max_ttl = 99;
        var timeout_ms = 2000;
        var probes_per_hop = 5;
        var no_dns = false;
        var json = false;

        for (var i = 0; i < Args.Length; i++)
        {
            var arg = Args[i];

            if (arg is "-v" or "--update" or "--upgrade")
                continue;

            if (arg.Equals("--no-dns", StringComparison.OrdinalIgnoreCase))
            {
                no_dns = true;
                continue;
            }

            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (TryReadIntOption(Args, ref i, "--max-ttl", out var max_ttl_value, out var max_ttl_error))
            {
                if (max_ttl_value is < 1 or > 255)
                    return new(null, 0, 0, 0, false, false, "Option --max-ttl must be in range [1..255]");

                max_ttl = max_ttl_value;
                continue;
            }

            if (max_ttl_error is not null)
                return new(null, 0, 0, 0, false, false, max_ttl_error);

            if (TryReadIntOption(Args, ref i, "--timeout-ms", out var timeout_ms_value, out var timeout_ms_error))
            {
                if (timeout_ms_value is < 100 or > 60000)
                    return new(null, 0, 0, 0, false, false, "Option --timeout-ms must be in range [100..60000]");

                timeout_ms = timeout_ms_value;
                continue;
            }

            if (timeout_ms_error is not null)
                return new(null, 0, 0, 0, false, false, timeout_ms_error);

            if (TryReadIntOption(Args, ref i, "--probes-per-hop", out var probes_per_hop_value, out var probes_per_hop_error))
            {
                if (probes_per_hop_value is < 1 or > 20)
                    return new(null, 0, 0, 0, false, false, "Option --probes-per-hop must be in range [1..20]");

                probes_per_hop = probes_per_hop_value;
                continue;
            }

            if (probes_per_hop_error is not null)
                return new(null, 0, 0, 0, false, false, probes_per_hop_error);

            if (arg.StartsWith("-", StringComparison.Ordinal))
                return new(null, 0, 0, 0, false, false, $"Unknown option: {arg}");

            if (host is not null)
                return new(null, 0, 0, 0, false, false, "Only one host value is allowed");

            host = arg;
        }

        return new(host, max_ttl, timeout_ms, probes_per_hop, no_dns, json, null);
    }

    /// <summary>Читает целочисленную опцию из аргументов</summary>
    /// <param name="Args">Список аргументов</param>
    /// <param name="Index">Текущий индекс аргумента</param>
    /// <param name="OptionName">Имя опции</param>
    /// <param name="Value">Прочитанное значение</param>
    /// <param name="Error">Сообщение об ошибке</param>
    /// <returns>Признак успешного чтения</returns>
    private static bool TryReadIntOption(string[] Args, ref int Index, string OptionName, out int Value, out string? Error)
    {
        ArgumentNullException.ThrowIfNull(Args);
        ArgumentException.ThrowIfNullOrWhiteSpace(OptionName);

        Value = 0;
        Error = null;

        var arg = Args[Index];
        if (arg.Equals(OptionName, StringComparison.OrdinalIgnoreCase))
        {
            if (Index + 1 >= Args.Length)
            {
                Error = $"Option {OptionName} requires value";
                return false;
            }

            Index++;
            var raw_value = Args[Index];
            if (!int.TryParse(raw_value, out Value))
            {
                Error = $"Option {OptionName} expects integer value";
                return false;
            }

            return true;
        }

        var option_prefix = OptionName + "=";
        if (arg.StartsWith(option_prefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw_value = arg[option_prefix.Length..];
            if (!int.TryParse(raw_value, out Value))
            {
                Error = $"Option {OptionName} expects integer value";
                return false;
            }

            return true;
        }

        return false;
    }
}
