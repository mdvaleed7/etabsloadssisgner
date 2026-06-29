# Advatech ETABS Automation Plugin

This folder now builds one ETABS plugin assembly:

```text
bin/Release/net8.0-windows/AdvatechEtabsPlugin.dll
```

The single plugin host exposes four tabs:

- Building Configuration: IS 1893 seismic inputs and IS 875 gravity-load inputs.
- Load Definition: creates load patterns, load cases, gravity assignments, and IS 456 / IS 875 combinations.
- Wind Automation: applies IS 875 Part 3 wind story forces using `wind-settings.json`.
- Slab Designer: extracts slab panels from ETABS, designs/optimizes them per IS 456:2000, and can push optimized thicknesses back into ETABS.

## Build

Build the consolidated plugin from the root project:

```powershell
dotnet build .\csietabsplugin.csproj -c Release
```

The project references `ETABSv1.dll` from:

```text
C:\Program Files\Computers and Structures\ETABS 22\ETABSv1.dll
```

If the DLL is not there, place a copy at:

```text
References\ETABSv1.dll
```

The project targets `net8.0-windows`, WinForms, and x64.

## ETABS Loading

Register/load this DLL in ETABS using the normal plugin manager workflow:

```text
bin/Release/net8.0-windows/AdvatechEtabsPlugin.dll
```

Keep `wind-settings.json` beside the DLL. The build copies the default settings file automatically.

## Wind Regression Test

The wind calculator still has a smoke test against cached workbook values:

```powershell
dotnet run --project .\src\EtabsWindAutomation.SmokeTests\EtabsWindAutomation.SmokeTests.csproj
```

## Engineering Note

This plugin automates model operations and design checks, but final load paths, force distribution assumptions, ETABS units, load combinations, and slab design results must be reviewed by the responsible structural engineer before production use.
