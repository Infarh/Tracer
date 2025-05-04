@echo off

dotnet publish -v d /p:PublishProfile=Properties/PublishProfiles/FolderProfile.pubxml

 if %errorlevel% neq 0 (
    echo Release build error
    pause
    exit /b 5
)

pause
