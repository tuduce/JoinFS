param(
    [string]$sourcePath,
    [string]$outputFile
)

# Create a hashtable to track directories
$script:directories = @{}
$script:directoryId = 1

function Get-DirectoryId {
    param([string]$relativePath)
    
    if ($relativePath -eq "") {
        return "INSTALLFOLDER"
    }
    
    if (-not $script:directories.ContainsKey($relativePath)) {
        $script:directories[$relativePath] = "dir_$($script:directoryId)"
        $script:directoryId++
    }
    
    return $script:directories[$relativePath]
}

$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
"@

# First, collect all directories
$allDirs = @()
Get-ChildItem -Path $sourcePath -Recurse -Directory | ForEach-Object {
    $relativePath = $_.FullName.Substring($sourcePath.Length + 1)
    $allDirs += $relativePath
    # Pre-register the directory ID
    $null = Get-DirectoryId $relativePath
}

# Sort directories by depth (shallowest first)
$allDirs = $allDirs | Sort-Object { ($_ -split '\\').Count }, $_

# Generate directory structure under INSTALLFOLDER
$xml += @"

    <DirectoryRef Id="INSTALLFOLDER">
"@

foreach ($dir in $allDirs) {
    $parentPath = Split-Path $dir -Parent
    if ($parentPath -eq "") {
        $dirId = Get-DirectoryId $dir
        $dirName = Split-Path $dir -Leaf
        $xml += @"

      <Directory Id="$dirId" Name="$dirName" />
"@
    }
}

$xml += @"

    </DirectoryRef>
"@

# Generate nested directories
foreach ($dir in $allDirs) {
    $parentPath = Split-Path $dir -Parent
    if ($parentPath -ne "") {
        $dirId = Get-DirectoryId $dir
        $dirName = Split-Path $dir -Leaf
        $parentId = Get-DirectoryId $parentPath
        
        $xml += @"

    <DirectoryRef Id="$parentId">
      <Directory Id="$dirId" Name="$dirName" />
    </DirectoryRef>
"@
    }
}

# Generate component group
$xml += @"

    <ComponentGroup Id="AllBuildFiles">
"@

$fileId = 1
Get-ChildItem -Path $sourcePath -Recurse -File | ForEach-Object {
    $compId = "comp_$fileId"
    $xml += @"

      <ComponentRef Id="$compId" />
"@
    $fileId++
}

$xml += @"

    </ComponentGroup>
  </Fragment>
  <Fragment>
"@

# Generate component definitions
$fileId = 1
Get-ChildItem -Path $sourcePath -Recurse -File | ForEach-Object {
    $guid = [guid]::NewGuid().ToString().ToUpper()
    $relativePath = $_.FullName.Substring($sourcePath.Length + 1)
    $dirPath = Split-Path $relativePath -Parent
    $dirId = Get-DirectoryId $dirPath
    $compId = "comp_$fileId"
    $fileIdStr = "file_$fileId"
    
    $xml += @"

    <DirectoryRef Id="$dirId">
      <Component Id="$compId" Guid="$guid">
        <File Id="$fileIdStr" Source="$($_.FullName)" KeyPath="yes" />
      </Component>
    </DirectoryRef>
"@
    $fileId++
}

$xml += @"

  </Fragment>
</Wix>
"@

[System.IO.File]::WriteAllText($outputFile, $xml)
Write-Host "Generated $($fileId - 1) file components in $($script:directories.Count) directories"
