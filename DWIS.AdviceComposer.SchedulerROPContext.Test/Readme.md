# DWIS.AdviceComposer.SchedulerROPContext.Test

## Overview
This project simulates scheduler context management for the ADCS Standard Auto-driller function in ROP composition tests.

Its main role is to publish `DWISContext.CapabilityPreferences` so `DWIS.AdviceComposer.Service` can select advisor outputs by feature.

## Context Logic Used by This Test
The worker monitors `MicroStates` from Blackboard and derives context as follows:
- If `InsideHardStringer == 2`:
  - `RigActionPlanFeature`
  - `DrillStemVibrationFeature`
- Otherwise:
  - `RigActionPlanFeature`
  - `CuttingsTransportFeature`

When the evaluated state changes, the worker writes a new serialized `DWISContext` to the Auto-driller context destination.

## Runtime Behavior
1. Connects to OPC UA Blackboard (`OPCUAURL`).
2. Subscribes to ADCS standard function descriptions.
3. Registers a query subscription for `MicroStates`.
4. Resolves/injects the Auto-driller context destination (and enable function placeholder when available).
5. On feature-state transition, publishes updated context preferences as JSON.

This makes it possible to switch composition preference between:
- `RigActionPlan + CuttingsTransport`
- `RigActionPlan + DrillStemVibration`

## Configuration
Configuration file path:
- local: `../home/config.json`
- container: `/home/config.json`

If missing, a default file is created.

Example config:
```json
{
  "LoopDuration": "00:00:01",
  "OPCUAURL": "opc.tcp://localhost:48030",
  "ContextChangePeriod": "00:00:30"
}
```

Parameter meaning:
- `LoopDuration`: polling/processing interval.
- `OPCUAURL`: OPC UA server endpoint.
- `ContextChangePeriod`: retained in config model for scenario control, but current implementation derives switches from microstate values instead of a timer.

## Run Locally
From repository root:
```powershell
dotnet restore .\DWIS.AdviceComposer.sln
dotnet run --project .\DWIS.AdviceComposer.SchedulerROPContext.Test\DWIS.AdviceComposer.SchedulerROPContext.Test.csproj
```

## Build and Run with Docker
Build image:
```powershell
docker build -f .\DWIS.AdviceComposer.SchedulerROPContext.Test\Dockerfile -t dwisadvicecomposerschedulerropcontexttest:latest .
```

Run on Windows:
```powershell
docker run -d --name schedulerropcontext -v C:\Volumes\DWISSchedulerROPContext:/home dwisadvicecomposerschedulerropcontexttest:latest
```

Run on Linux:
```bash
docker run -d --name schedulerropcontext -v /home/Volumes/DWISSchedulerROPContext:/home dwisadvicecomposerschedulerropcontexttest:latest
```

## How to Use in ROP Composition Tests
1. Start Blackboard OPC UA endpoint.
2. Start `DWIS.AdviceComposer.Service`.
3. Start the three advisor test workers:
   - `DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test`
   - `DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test`
   - `DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test`
4. Start this scheduler context test.
5. Observe composed outputs from AdviceComposer changing as context preference changes with microstate transitions.

## Validation Checklist
- Log entries show `context changed to:` with the expected feature set.
- Blackboard receives serialized `DWISContext` updates when `InsideHardStringer` state toggles.
- AdviceComposer selection behavior follows the active context preferences.

## Troubleshooting
- No context updates: verify microstate signal is present and query subscription resolves a live value.
- Context not reflected in composition: verify AdviceComposer is connected to the same Blackboard endpoint and context semantic destination is correctly injected/resolved.
- OPC UA write failures: check certificate/trust configuration in `config/Quickstarts.ReferenceClient.Config.xml`.
