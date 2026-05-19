namespace Tracer.ConsoleOutput;

/// <summary>Потокобезопасный вывод в консоль</summary>
internal static class ConsoleWriter
{
    private static readonly Lock __ConsoleLock = new();

    /// <summary>Проверяет доступность позиционирования курсора</summary>
    /// <returns>Признак что операции курсора доступны</returns>
    public static bool CanUseCursorControl()
    {
        if (Console.IsOutputRedirected)
            return false;

        try
        {
            _ = Console.GetCursorPosition();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Возвращает текущую строку курсора или fallback</summary>
    /// <param name="FallbackLine">Значение по умолчанию</param>
    /// <returns>Позиция курсора или fallback</returns>
    public static int GetCursorTopOrFallback(int FallbackLine = 0)
    {
        if (Console.IsOutputRedirected)
            return FallbackLine;

        try
        {
            return Console.CursorTop;
        }
        catch (IOException)
        {
            return FallbackLine;
        }
        catch (InvalidOperationException)
        {
            return FallbackLine;
        }
    }

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

        if (!CanUseCursorControl())
            return;

        lock (__ConsoleLock)
        {
            try
            {
                var (col, line) = Console.GetCursorPosition();

                Console.SetCursorPosition(Col, Line);
                Console.Write(Str);
                Console.SetCursorPosition(col, line);
            }
            catch (IOException)
            {
                // Мягко пропускаем позиционный вывод в non-interactive окружениях
            }
            catch (InvalidOperationException)
            {
                // Мягко пропускаем позиционный вывод в non-interactive окружениях
            }
        }
    }
}
