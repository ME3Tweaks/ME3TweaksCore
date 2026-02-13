$projectRoot = (Get-Item $PSScriptRoot).parent.FullName
$buildRoot = Join-Path -Path $projectRoot -ChildPath "Build"
$lzmaExe = Join-Path -Path $projectRoot -ChildPath "Build" | Join-Path -ChildPath "lzma.exe"
$localizationDir = Join-Path -Path $projectRoot -ChildPath "Localization" | Join-Path -ChildPath "Dictionaries"
$filesToCompress = Get-ChildItem $localizationDir -Filter "*.xaml"

foreach ($xaml in $filesToCompress) {
$hashFile = Join-Path -Path $buildRoot -ChildPath "$($xaml.Name).hash"
$xamlFile = $xaml.FullName
$lzmaFile = $xaml.FullName + ".lzma"
$needsCompiled = $true
$tempFile = $null
$fileToProcess = $xamlFile

# Special handling for int.xaml - strip comments before hashing/compression
if ($xaml.Name -eq "int.xaml") {
    try {
        $xml = [xml](Get-Content $xamlFile)
        $tempFile = Join-Path -Path $env:TEMP -ChildPath "int_temp.xaml"
        $xml.Save($tempFile)
        $fileToProcess = $tempFile
    }
    catch {
        Write-Warning "Failed to process int.xaml as XML: $_"
        $fileToProcess = $xamlFile
    }
}

    if ((Test-Path $lzmaFile) -eq $false){
        # LZMA file doesn't exist - it needs compiled
    }
    elseif (Test-Path $hashFile)
    {
        # Local builds can skip compilation if hash file is up to date
        $hashes = Get-Content $hashFile
        $hashLast = $hashes[0]

        $currentHash = Get-FileHash $fileToProcess -Algorithm SHA256
        $needsCompiled = $hashLast -ne $currentHash.Hash;

        # Check LZMA
        $hashLastLzma = $hashes[1]
        $currentHashLzma = Get-FileHash $lzmaFile -Algorithm SHA256
        if ($needsCompiled -eq $false){ # We must compute this anyways as it may be stored
            $needsCompiled = $hashLastLzma -ne $currentHashLzma.Hash;
        }

        #Write-Output "HASHES for $($xaml.Name):"
        #$currentHash
        #$currentHashLzma

        # If hash file exists we can check if it doesn't need compiled. If it doesn't, it will always need compiled
        if ($needsCompiled -eq $false)
        {
            Write-Output "Skipping compression of localization file $($xaml.Name) as hash is already up to date"
            if ($tempFile -and (Test-Path $tempFile)) {
                Remove-Item $tempFile
            }
            continue # Skip this compilation
        }
    }

    $inname = "`"" + $fileToProcess + "`""
    $outname = "`"" + $xaml.FullName + ".lzma`""
    $processOptions = @{
        FilePath = $lzmaExe
        Wait = $true
        NoNewWindow = $true
        ArgumentList = "e", $inname, $outname
    }
    $processOptions.FilePath
    Start-Process @processOptions

    $currentHashLzma = Get-FileHash $lzmaFile -Algorithm SHA256
    Set-Content -Path $hashFile -Value "$($currentHash.Hash)`n$($currentHashLzma.Hash)"

    # Clean up temp file if it was created
    if ($tempFile -and (Test-Path $tempFile)) {
        Remove-Item $tempFile
    }
}
