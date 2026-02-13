# ME3TweaksCore

[![GitHub](https://img.shields.io/github/license/ME3Tweaks/ME3TweaksCore)](https://github.com/ME3Tweaks/ME3TweaksCore)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)

ME3TweaksCore is a .NET library providing core functionality across various ME3Tweaks software tools for modding the Mass Effect games. It does not provide the mod management functionality provided by ME3Tweaks Mod Manager.

## Features

### Core Library (ME3TweaksCore)

- **Game Target Management**: Support for Mass Effect trilogy (ME1, ME2, ME3) and Legendary Edition (LE1, LE2, LE3)
- **Mod Management**: 
  - ASI mod support and management
  - Third-party mod identification (TPMI)
- **M3Merge System**: Advanced merging capabilities for:
  - Bio2DA tables
  - Global shaders
  - Plot manager
  - Squadmate outfits
  - LE1 configuration
  - ME2 email system
- **Diagnostics & Logging**:
  - Diagnostic tools
  - Log collection and upload
- **Backup & Restore Services**:
  - Game backup creation
  - Game restoration from backups
  - File source management
- **Texture Management**:
  - Texture overrides (M3TO)
  - Texture mod installation tracking (via MEM)
- **Game Filesystem Operations**:
  - File validation and verification
  - SFAR archive manipulation
  - Game target system

### WPF Extensions (ME3TweaksCoreWPF)

- **UI Components**: WPF-specific controls and utilities
- **Log Viewer**: Web-based log viewing interface
- **Visual Helpers**: UI converters and services

## Requirements

- **.NET 10.0 or later** (Windows platform)
- **Windows operating system**
- **Visual Studio 2026 or later** (for development)
- **x64 architecture**

### Dependencies

The library includes the following submodules and packages:

**Submodules:**
- [LegendaryExplorer](https://github.com/ME3Tweaks/LegendaryExplorer) - Core Mass Effect package handling
- [ComputerInfo](https://github.com/ME3Tweaks/ComputerInfo) - System information utilities
- [RoboSharp](https://github.com/Mgamerz/RoboSharp) - Robust file copying for restore operations
- [AuthenticodeExaminer](https://github.com/ME3Tweaks/AuthenticodeExaminer) - Code signing verification
Submodules may not be up to date, and may need manually updated to build.

## Key Components

### ME3TweaksCoreLib

The main initialization class for the library. You must call `Initialize()` before using any library features:

```csharp
using ME3TweaksCore;

var initPackage = new ME3TweaksCoreLibInitPackage
{
    // Configure initialization parameters
    GetLogger = () => yourLogger,
    LoadAuxiliaryServices = true,
    // ... other options
};

ME3TweaksCoreLib.Initialize(initPackage);
```

### GameTarget

Represents a Mass Effect game installation. Provides access to game files, DLC, and configuration:

```csharp
var gameTarget = new GameTarget(MEGame.LE3, gamePath, registryActive: false);
```

### M3Merge

The merging system allows mods to modify game assets in a compatible way:
- **Bio2DAMerge**: Merge 2DA database tables
- **PlotManagerMerge**: Merge plot manager data
- **SQMOutfitMerge**: Merge squadmate outfit configurations
- **GlobalShaderMerge**: Merge shader configurations

### Localizations

The project includes a PowerShell script that runs before compilation to prepare localization files. Your system must allow running powershell scripts in order for build to succeed.

```powershell
# Automatically run during build
.\ME3TweaksCore\Build\preparelocalizations.ps1
```

## Related Projects

- [ME3Tweaks Mod Manager](https://github.com/ME3Tweaks/ME3TweaksModManager) - The flagship mod manager for Mass Effect games
- [LegendaryExplorer](https://github.com/ME3Tweaks/LegendaryExplorer) - Toolset for editing Mass Effect packages

## Support

For support and questions:
- Visit the [ME3Tweaks Discord](https://discord.gg/me3tweaks)
- Check the [ME3Tweaks website](https://me3tweaks.com)
- Open an issue on GitHub

## Acknowledgments

This library builds upon and integrates with several open-source projects and the Mass Effect modding community's collective knowledge. Special thanks to all contributors and the Mass Effect modding community.
