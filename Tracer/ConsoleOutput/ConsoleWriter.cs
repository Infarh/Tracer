namespace Tracer.ConsoleOutput;

/// <summary>Потокобезопасный вывод в консоль</summary>
internal static class ConsoleWriter
{
    private static readonly Lock __ConsoleLock = new();

    /// <summary>Пишет строку в консоль</summary>
    /// <param name="Message">Текст сообщения</param>
    public static void WriteLine(string Message)
    {
        ArgumentNullException.ThrowIfNull(Message);

        lock (__ConsoleLock)
            Console.WriteLine(Message);
    }

    /// <summary>Пишет строку в указанную позицию консоли</summary>
    /// <param name="Line">Номер строки</param>
    /// <param name="Col">Номер столбца</param>
    /// <param name="Str">Текст для вывода</param>
    public static void Write(int Line, int Col, string Str)
    {
        ArgumentNullException.ThrowIfNull(Str);

        if (Console.IsOutputRedirected)
            return;

        lock (__ConsoleLock)
        {
            var (col, line) = Console.GetCursorPosition();

            Console.SetCursorPosition(Col, Line);
            Console.Write(Str);
            Console.SetCursorPosition(col, line);
        }
    }
}
