# UpdateCachedContent.ps1
# Downloads and updates the ASI manifest from ME3Tweaks server
# Also downloads ASI files marked for M3 deployment

$ErrorActionPreference = "Stop"

# Configuration
$manifestUrl = "https://me3tweaks.com/mods/asi/getmanifest?AllGames=1&M3=1"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetFile = Join-Path $scriptDir "..\NativeMods\CachedASI\asimanifest.xml"
$nativeModsDir = Join-Path $scriptDir "..\NativeMods\CachedASI"

# Game folder mapping
$gameFolders = @{
    "1" = "ME1"
    "2" = "ME2"
    "3" = "ME3"
    "4" = "LE1"
    "5" = "LE2"
    "6" = "LE3"
    "7" = "LEL"
}

# Function to calculate MD5 hash
function Get-FileMD5 {
    param([string]$filePath)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $stream = [System.IO.File]::OpenRead($filePath)
    try {
        $hash = $md5.ComputeHash($stream)
        return [System.BitConverter]::ToString($hash).Replace("-", "").ToLower()
    }
    finally {
        $stream.Close()
    }
}

Write-Host "ASI Manifest Updater & Deployer" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Downloading manifest from: $manifestUrl" -ForegroundColor Yellow

try {
    # Download the manifest
    $webClient = New-Object System.Net.WebClient
    $xmlContent = $webClient.DownloadString($manifestUrl)
    
    Write-Host "Downloaded manifest successfully" -ForegroundColor Green
    Write-Host "Validating XML..." -ForegroundColor Yellow
    
    # Validate that it's valid XML
    try {
        $xmlDoc = [xml]$xmlContent
        
        # Additional validation - check that root element is ASIManifest
        if ($xmlDoc.DocumentElement.Name -ne "ASIManifest") {
            throw "Invalid XML structure: Root element is not 'ASIManifest'"
        }
        
        Write-Host "XML validation passed" -ForegroundColor Green
        
        # Resolve the full path
        $targetFilePath = Resolve-Path $targetFile -ErrorAction SilentlyContinue
        if (-not $targetFilePath) {
            $targetFilePath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $targetFile))
        }
        
        Write-Host "Target file: $targetFilePath" -ForegroundColor Yellow
        
        # Write the new manifest
        $xmlContent | Out-File -FilePath $targetFilePath -Encoding UTF8 -Force
        
        Write-Host ""
        Write-Host "Manifest updated successfully!" -ForegroundColor Green
        
        # Display some statistics
        $updateGroups = $xmlDoc.SelectNodes("//updategroup")
        Write-Host ""
        Write-Host "Manifest contains:" -ForegroundColor Cyan
        Write-Host "  - $($updateGroups.Count) update groups" -ForegroundColor White
        
        $asiMods = $xmlDoc.SelectNodes("//asimod")
        Write-Host "  - $($asiMods.Count) total ASI mod versions" -ForegroundColor White
        
        # Process ASI deployments
        Write-Host ""
        Write-Host "Processing M3 Deployable ASIs..." -ForegroundColor Cyan
        Write-Host "================================" -ForegroundColor Cyan
        
        $deployableGroups = 0
        $downloadedCount = 0
        $failedCount = 0
        
        foreach ($group in $updateGroups) {
            $groupId = $group.GetAttribute("groupid")
            $gameId = $group.GetAttribute("game")
            
            # AutoTOC LE is shared and has special handling in code.
            if ($groupId -eq "29" -or $groupId -eq "30" -or $groupId -eq "31") {
                continue
            }
            
            # Check if any ASI in this group has m3cdeploy=1
            $deployableASIs = $group.SelectNodes("asimod[m3cdeploy='1']")
            
            if ($deployableASIs.Count -eq 0) {
                continue
            }
            
            $deployableGroups++
            
            # Find the highest version ASI in the entire group (not just those with m3cdeploy=1)
            # This handles the case where v3 has m3cdeploy but v4 doesn't - we still want v4
            $allASIs = $group.SelectNodes("asimod")
            $highestVersion = $null
            $highestVersionNumber = -1
            
            foreach ($asi in $allASIs) {
                $version = [int]$asi.version
                if ($version -gt $highestVersionNumber) {
                    $highestVersionNumber = $version
                    $highestVersion = $asi
                }
            }
            
            if ($null -eq $highestVersion) {
                continue
            }
            
            # Get ASI details
            $asiName = $highestVersion.name
            $asiVersion = $highestVersion.version
            $downloadUrl = $highestVersion.downloadlink
            $expectedHash = $highestVersion.hash
            $installedName = $highestVersion.installedname
            
            # Determine target folder
            $gameFolder = $gameFolders[$gameId]
            if (-not $gameFolder) {
                Write-Host "  [WARNING] Unknown game ID: $gameId for ASI: $asiName" -ForegroundColor Yellow
                continue
            }
            
            $targetFolder = Join-Path $nativeModsDir $gameFolder
            
            # Create folder if it doesn't exist
            if (-not (Test-Path $targetFolder)) {
                New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
            }
            
            # Determine target file name using pattern: [InstalledName]-v[VERSION].asi
            $fileName = "$installedName-v$asiVersion.asi"
            $targetFilePath = Join-Path $targetFolder $fileName
            
            # Check if file already exists and matches hash
            $shouldDownload = $true
            if (Test-Path $targetFilePath) {
                $existingHash = Get-FileMD5 -filePath $targetFilePath
                if ($existingHash -eq $expectedHash) {
                    Write-Host "  Already up to date: $asiName v$asiVersion ($gameFolder)" -ForegroundColor Gray
                    $shouldDownload = $false
                    $downloadedCount++
                }
                else {
                    Write-Host "  Removing outdated file: $fileName" -ForegroundColor Gray
                    Remove-Item $targetFilePath -Force
                }
            }
            
            # Clean any other ASI files in target folder with the same installed name but different filename
            $existingFiles = Get-ChildItem -Path $targetFolder -Filter "*.asi" -ErrorAction SilentlyContinue
            foreach ($file in $existingFiles) {
                if ($file.Name -ne $fileName -and $file.BaseName -eq $installedName) {
                    Write-Host "  Removing old variant: $($file.Name)" -ForegroundColor Gray
                    Remove-Item $file.FullName -Force
                }
            }
            
            # Download ASI if needed
            if ($shouldDownload) {
                Write-Host "  Downloading: $asiName v$asiVersion ($gameFolder)" -ForegroundColor Yellow
                Write-Host "    URL: $downloadUrl" -ForegroundColor Gray
                
                try {
                    $webClient.DownloadFile($downloadUrl, $targetFilePath)
                    
                    # Verify hash
                    $actualHash = Get-FileMD5 -filePath $targetFilePath
                    
                    if ($actualHash -eq $expectedHash) {
                        Write-Host "    Downloaded and verified: $fileName" -ForegroundColor Green
                        $downloadedCount++
                    }
                    else {
                        Write-Host "    [ERROR] Hash mismatch!" -ForegroundColor Red
                        Write-Host "      Expected: $expectedHash" -ForegroundColor Red
                        Write-Host "      Actual:   $actualHash" -ForegroundColor Red
                        Remove-Item $targetFilePath -Force
                        $failedCount++
                    }
                }
                catch {
                    Write-Host "    [ERROR] Download failed: $_" -ForegroundColor Red
                    $failedCount++
                }
            }
        }
        
        Write-Host ""
        Write-Host "Deployment Summary:" -ForegroundColor Cyan
        Write-Host "  - Groups with deployable ASIs: $deployableGroups" -ForegroundColor White
        Write-Host "  - Successfully downloaded: $downloadedCount" -ForegroundColor Green
        if ($failedCount -gt 0) {
            Write-Host "  - Failed: $failedCount" -ForegroundColor Red
        }
        
    }
    catch {
        Write-Host "XML validation failed: $_" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Error downloading manifest: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
