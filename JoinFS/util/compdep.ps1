dotnet build .\JoinFS.csproj -c CONSOLE /p:PlatformTarget=ARM64
dotnet build .\Installer\JoinFS.wixproj -c CONSOLE /p:PlatformTarget=ARM64
scp .\Installer\bin\CONSOLE\JoinFS-CONSOLE.zip tuduce@minion:
