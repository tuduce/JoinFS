cd ..\JoinFS\

$configs = @('FS2024', 'FS2020', 'FSX', 'P3D', 'XPLANE', 'CONSOLE')

foreach($config in $configs) {
	Write-Host "Building configuration: $config"
	dotnet build .\JoinFS.csproj -c $config
}

foreach($config in $configs) {
	Write-Host "Building installer for configuration: $config"
	dotnet build .\Installer\JoinFS.wixproj -c $config
}
