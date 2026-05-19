namespace Tracer.Cli;

/// <summary>Структурный парсер аргументов командной строки</summary>
internal static class MainOptionsParser
{
    private const int __DefaultMaxTtl = 99;
    private const int __DefaultTimeoutMs = 2000;
    private const int __DefaultProbesPerHop = 5;

    /// <summary>Разбирает аргументы в типизированные опции</summary>
    /// <param name="Args">Входные аргументы командной строки</param>
    /// <returns>Результат разбора аргументов</returns>
    public static MainOptions Parse(string[] Args)
    {
        ArgumentNullException.ThrowIfNull(Args);

        var state = new ParseState();

        for (var i = 0; i < Args.Length; i++)
        {
            var arg = Args[i];

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (state.Host is not null)
                    return CreateError("Only one host value is allowed");

                state.Host = arg;
                continue;
            }

            if (TryHandleFlagOption(arg, state))
                continue;

            if (TryParseIntOption(Args, ref i, "--max-ttl", out var max_ttl_value, out var max_ttl_error))
            {
                if (max_ttl_value is < 1 or > 255)
                    return CreateError("Option --max-ttl must be in range [1..255]");

                state.MaxTtl = max_ttl_value;
                continue;
            }

            if (max_ttl_error is not null)
                return CreateError(max_ttl_error);

            if (TryParseIntOption(Args, ref i, "--timeout-ms", out var timeout_ms_value, out var timeout_ms_error))
            {
                if (timeout_ms_value is < 100 or > 60000)
                    return CreateError("Option --timeout-ms must be in range [100..60000]");

                state.TimeoutMs = timeout_ms_value;
                continue;
            }

            if (timeout_ms_error is not null)
                return CreateError(timeout_ms_error);

            if (TryParseIntOption(Args, ref i, "--probes-per-hop", out var probes_per_hop_value, out var probes_per_hop_error))
            {
                if (probes_per_hop_value is < 1 or > 20)
                    return CreateError("Option --probes-per-hop must be in range [1..20]");

                state.ProbesPerHop = probes_per_hop_value;
                continue;
            }

            if (probes_per_hop_error is not null)
                return CreateError(probes_per_hop_error);

            return CreateError($"Unknown option: {arg}");
        }

        return new(
            state.Host,
            state.MaxTtl,
            state.TimeoutMs,
            state.ProbesPerHop,
            state.NoDns,
            state.Json,
            state.ShowVersion,
            state.RunUpdate,
            null);
    }

    /// <summary>Обрабатывает флаговые опции без значения</summary>
    /// <param name="Arg">Текущий аргумент</param>
    /// <param name="State">Текущее состояние парсинга</param>
    /// <returns>Признак, что аргумент обработан</returns>
    private static bool TryHandleFlagOption(string Arg, ParseState State)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Arg);
        ArgumentNullException.ThrowIfNull(State);

        if (Arg.Equals("--no-dns", StringComparison.OrdinalIgnoreCase))
        {
            State.NoDns = true;
            return true;
        }

        if (Arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
        {
            State.Json = true;
            return true;
        }

        if (Arg is "-v" or "--version")
        {
            State.ShowVersion = true;
            return true;
        }

        if (Arg is "--update" or "--upgrade")
        {
            State.RunUpdate = true;
            return true;
        }

        return false;
    }

    /// <summary>Разбирает целочисленную опцию вида --name value или --name=value</summary>
    /// <param name="Args">Массив аргументов</param>
    /// <param name="Index">Текущий индекс аргумента</param>
    /// <param name="OptionName">Имя опции</param>
    /// <param name="Value">Распарсенное значение</param>
    /// <param name="Error">Сообщение об ошибке</param>
    /// <returns>Признак того что опция распознана</returns>
    private static bool TryParseIntOption(string[] Args, ref int Index, string OptionName, out int Value, out string? Error)
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
            return TryParseIntValue(Args[Index], OptionName, out Value, out Error);
        }

        var option_prefix = OptionName + "=";
        if (!arg.StartsWith(option_prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var raw_value = arg[option_prefix.Length..];
        return TryParseIntValue(raw_value, OptionName, out Value, out Error);
    }

    /// <summary>Разбирает числовое значение опции</summary>
    /// <param name="RawValue">Строка значения</param>
    /// <param name="OptionName">Имя опции</param>
    /// <param name="Value">Распарсенное значение</param>
    /// <param name="Error">Сообщение об ошибке</param>
    /// <returns>Признак успешного разбора</returns>
    private static bool TryParseIntValue(string RawValue, string OptionName, out int Value, out string? Error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OptionName);

        Value = 0;
        if (!int.TryParse(RawValue, out Value))
        {
            Error = $"Option {OptionName} expects integer value";
            return false;
        }

        Error = null;
        return true;
    }

    /// <summary>Создает ошибочный результат парсинга</summary>
    /// <param name="ErrorMessage">Сообщение об ошибке</param>
    /// <returns>Опции с заполненной ошибкой</returns>
    private static MainOptions CreateError(string ErrorMessage) =>
        new(null, 0, 0, 0, false, false, false, false, ErrorMessage);

    /// <summary>Внутреннее состояние разбора аргументов</summary>
    private sealed class ParseState
    {
        /// <summary>Имя хоста назначения</summary>
        public string? Host { get; set; }

        /// <summary>Максимальное TTL</summary>
        public int MaxTtl { get; set; } = __DefaultMaxTtl;

        /// <summary>Таймаут ping в миллисекундах</summary>
        public int TimeoutMs { get; set; } = __DefaultTimeoutMs;

        /// <summary>Количество проб на хоп</summary>
        public int ProbesPerHop { get; set; } = __DefaultProbesPerHop;

        /// <summary>Флаг отключения DNS</summary>
        public bool NoDns { get; set; }

        /// <summary>Флаг JSON-вывода</summary>
        public bool Json { get; set; }

        /// <summary>Флаг показа версии</summary>
        public bool ShowVersion { get; set; }

        /// <summary>Флаг запуска обновления</summary>
        public bool RunUpdate { get; set; }
    }
}
