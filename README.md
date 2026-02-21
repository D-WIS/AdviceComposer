# AdviceComposer

## Overview
AdviceComposer is a .NET 8 solution for composing ADCS/DWIS advisory signals on an OPC UA Blackboard.

The solution contains:
- A composition service (`DWIS.AdviceComposer.Service`) that selects, merges, and publishes composed outputs.
- A shared model library (`DWIS.AdviceComposer.Model`).
- Three ROP advisor test publishers with different feature semantics.
- One scheduler/context test publisher that drives feature-preference changes.

Together, these projects provide an end-to-end test bench for ROP signal composition.

## Solution Projects
- `DWIS.AdviceComposer.Service`
Purpose: Main background worker that composes controller/procedure/FDIR/SOE outputs.
Readme: `DWIS.AdviceComposer.Service/Readme.md`

- `DWIS.AdviceComposer.Model`
Purpose: Shared model contracts used by the service composition pipeline.
Readme: `DWIS.AdviceComposer.Model/Readme.md`

- `DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test`
Purpose: Publishes synthetic ROP advisory signals tagged with `CuttingsTransportFeature`.
Readme: `DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test/Readme.md`

- `DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test`
Purpose: Publishes synthetic ROP advisory signals tagged with `DrillStemVibrationFeature`.
Readme: `DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/Readme.md`

- `DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test`
Purpose: Publishes synthetic ROP limit signals tagged with `RigActionPlanFeature`.
Readme: `DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test/Readme.md`

- `DWIS.AdviceComposer.SchedulerROPContext.Test`
Purpose: Publishes `DWISContext.CapabilityPreferences` from microstate conditions to drive advisor selection.
Readme: `DWIS.AdviceComposer.SchedulerROPContext.Test/Readme.md`

## How Composition Is Tested End-to-End
1. Advisor test workers publish feature-tagged synthetic recommendations to Blackboard.
2. Scheduler context worker publishes active capability preferences.
3. AdviceComposer service subscribes to activable functions, reads context and candidate values, selects/combines best candidates, and writes composed outputs.

Typical context preference sets used in tests:
- `RigActionPlanFeature + CuttingsTransportFeature`
- `RigActionPlanFeature + DrillStemVibrationFeature`

## Prerequisites
- .NET 8 SDK
- Running DWIS OPC UA Blackboard endpoint
- Certificate/trust configuration suitable for your environment (see each project `config/Quickstarts.ReferenceClient.Config.xml`)

## Build
From repository root:
```powershell
dotnet restore .\DWIS.AdviceComposer.sln
dotnet build .\DWIS.AdviceComposer.sln -c Release
```

## Quick Start (Local, Multi-Process)
Run these in separate terminals from repository root, in this order:

1. Start the composition service:
```powershell
dotnet run --project .\DWIS.AdviceComposer.Service\DWIS.AdviceComposer.Service.csproj
```

2. Start advisor publishers:
```powershell
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test.csproj
```
```powershell
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test.csproj
```
```powershell
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj
```

3. Start scheduler context publisher:
```powershell
dotnet run --project .\DWIS.AdviceComposer.SchedulerROPContext.Test\DWIS.AdviceComposer.SchedulerROPContext.Test.csproj
```

## Configuration Model
Each worker reads/writes a `config.json` in:
- local run: `../home/config.json` (relative to project execution directory)
- container run: `/home/config.json` via mounted volume

Common keys across projects:
- `LoopDuration`
- `OPCUAURL`

Advisor test projects also define sinusoid parameters (`Amplitude`, `Average`, `Period`) for flowrate, rotational speed, ROP, WOB, TOB, and differential pressure.

## Docker Notes
Each project includes its own `Dockerfile` and can run independently as a container.
Use the project-level README files for exact image tags and run commands.

## Repository Structure
- `DWIS.AdviceComposer.sln`
- `DWIS.AdviceComposer.Service/`
- `DWIS.AdviceComposer.Model/`
- `DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test/`
- `DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/`
- `DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test/`
- `DWIS.AdviceComposer.SchedulerROPContext.Test/`
- `home/`
- `.github/workflows/`

## Troubleshooting
- No writes to Blackboard:
  - Verify `OPCUAURL` in each project's `config.json`.
  - Confirm all processes point to the same Blackboard endpoint.
- Missing composed outputs:
  - Ensure all three advisor test workers are running.
  - Ensure scheduler context worker is running and publishing preferences.
- Certificate/connection issues:
  - Validate trust/certificate settings from the `Quickstarts.ReferenceClient.Config.xml` files.

## License
See `LICENSE` files in each project directory.
