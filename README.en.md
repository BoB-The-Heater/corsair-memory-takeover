# Corsair Memory Takeover

Check Corsair RGB RAM integration with motherboard software and maintain confirmed vendor services. Lighting effects stay in the official software.

[Download](https://github.com/BoB-The-Heater/corsair-memory-takeover/releases/tag/v0.3.1) · [Website](https://bob-the-heater.github.io/corsair-memory-takeover/en/) · [简体中文](README.md)

## Support

| Software | Features |
| --- | --- |
| ASUS Aura Sync | Component detection and confirmed service maintenance (preview) |
| MSI Mystic Light | Component detection and confirmed service maintenance (preview) |
| GIGABYTE RGB Fusion | Detection and official setup guidance |
| Other brands | Detection and setup guidance |

Adapters are previews. Compatibility is not guaranteed; running services do not prove physical RGB synchronization.

## Use

1. Download the Windows x64 ZIP, extract it and run `CorsairTakeover.exe`.
2. Follow the brand setup guide to install official components and approve control.
3. ASUS/MSI users can enable maintenance after confirming physical lighting control. Use Undo to remove maintenance.

Requires .NET Framework 4.8. The UI is Simplified Chinese. The first scan needs no elevation; system changes request administrator rights. Binaries are unsigned; release checksums are provided.

## Build

```powershell
.\build.ps1
.\run-tests.ps1
```

Uses the Windows .NET Framework compiler with no NuGet dependencies. Tests use simulated services, a simulated registry and synthetic logs. Edit website copy in `scripts/build-site.py` and run it with Python 3 to regenerate `docs/`.

[Setup guide (Chinese)](GUIDE.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [MIT License](LICENSE)

The application has no report upload feature. Independent project, not affiliated with hardware vendors.
