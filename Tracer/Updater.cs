using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Reflection;

namespace Tracer;

internal static class Updater
{
    private const string __UrlApiReleasesLatest = "https://api.github.com/repos/Infarh/Tracer/releases/latest";

    public static Version CurrentVersion => Version.Parse(typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "1.0");

    public static async Task<ReleaseInfo> GetLastServerReleaseAsync()
    {

        using var http = new HttpClient();

        // Устанавливаем заголовок User-Agent, так как GitHub API требует его
        http.DefaultRequestHeaders.UserAgent.Add(new("TracerUpdater", "1.0"));

        try
        {
            var response = await http.GetAsync(__UrlApiReleasesLatest);

            var release_info = await response
                .EnsureSuccessStatusCode()
                .Content
                .ReadFromJsonAsync<ReleaseInfo>(options: ReleaseInfoSerializationContext.Default.Options)
                .ConfigureAwait(false);

            if (release_info is not null)
                return release_info;

            throw new InvalidOperationException("Get release version from server error.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error retrieving the latest release version from the server.", ex);
        }
    }

    public static async Task UpdateAsync()
    {
        if (await GetLastServerReleaseAsync().ConfigureAwait(false) is not { Version: var server_version, Assets: { Count: > 0 } release_assets } release_info)
        {
            Console.WriteLine("Error when receiving release information from the server.");
            return;
        }

        var current_version = CurrentVersion;
        Console.WriteLine($"Current version: {current_version}");
        Console.WriteLine($"Server version : {server_version}");

        if (current_version >= server_version)
        {
            Console.WriteLine("Server version no greater than current version.");
            Console.WriteLine("No update is required.");
            return;
        }

        var artefact = release_assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (artefact is null)
            return;

        if (Process.GetCurrentProcess() is not { Id: var current_pid, MainModule: { FileName: { } process_file_path } })
            return;

        var program_file = new FileInfo(process_file_path);
        program_file.CopyTo($"{program_file}.bak", true);

        var new_program_file_path = Path.ChangeExtension(program_file.FullName, $"[{server_version}].new.exe");
        await artefact.DownloadAsync(new_program_file_path).ConfigureAwait(false);

        var updater_script_path = Path.Combine(program_file.DirectoryName!, "updater.bat");
        await File.WriteAllTextAsync(updater_script_path, __CreateScriptTest).ConfigureAwait(false);

        var start_info = new ProcessStartInfo
        {
            FileName = updater_script_path,
            Arguments = $"{current_pid} \"{program_file}\" \"{new_program_file_path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process.Start(start_info);

        await Task.Delay(2000).ConfigureAwait(false); // Требуется для того, что бы скрипт успел убить программу.
    }

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private const string __CreateScriptTest = """
        @echo off
        setlocal
        
        echo Update process started... > update.log
        echo %~0 %~1 %~2 %~3 >> update.log
        
        :: Проверка количества параметров
        if "%~1"=="" (
          echo Error: You must specify the PID of the process to be updated. >> update.log
          exit /b 1
        )
        
        if "%~2"=="" (
          echo Error: You must specify the name of the current file. >> update.log
          exit /b 1
        )
        
        if "%~3"=="" (
          echo Error: You must specify the name of the new file. >> update.log
          exit /b 1
        )
       
        :: Получение параметров
        set "pid=%~1"
        set "current_file=%~2"
        set "new_file=%~3"
        
        echo Completion of the process with PID %pid%... >> update.log
        taskkill /F /PID %pid% >> update.log
        echo tatskkill errorlevel: %errorlevel% >> update.log
        if %errorlevel% neq 0 (
            echo Error: The process could not be completed with PID %pid%. >> update.log
            exit /b 2
        )
        
        echo Deleting an old file... >> update.log
        del "%current_file%" >> update.log
        if %errorlevel% neq 0 (
            echo Error: The old file could not be deleted.>> update.log
            exit /b 3
        )
        
        echo Copying a new file... >> update.log
        copy "%new_file%" "%current_file%" >> update.log
        if %errorlevel% neq 0 (
            echo Error: The new file could not be copied. >> update.log
            exit /b 4
        )
        
        echo Deleting the new version file... >> update.log
        del "%new_file%" >> update.log
        if %errorlevel% neq 0 (
            echo Error: The new version file could not be deleted. >> update.log
            exit /b 5
        )
        
        echo The program has been updated. %current_file% >> update.log
        
        :: Запуск обновленной программы с ключом -v
        start %current_file% -v >> update.log
        
        pause
        
        :: Самоуничтожение скрипта
        del "%~f0"
        
        endlocal
        exit /b 0
        """;
}