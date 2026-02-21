# DWIS.AdviceComposer.Service

## Overview
`DWIS.AdviceComposer.Service` is a .NET Worker Service that connects to a DWIS Blackboard through OPC UA and composes advice outputs from available advisor signals.

It supports four capability families:
- Controller functions (controller parameters, set-points, limits)
- Procedure functions
- Fault Detection, Isolation and Recovery (FDIR) functions
- Safe Operating Envelope (SOE) functions

The service listens for activable functions, discovers their semantic inputs through SPARQL queries, subscribes to live values, selects/combines the best advice based on context features, and writes composed outputs back to Blackboard variables.

## Main Responsibilities
- Register to the ADCS standard interface stream (activable functions as JSON payloads).
- Parse and classify each activable function by type.
- Register required semantic queries for context and data sources.
- Ensure destination variables exist in Blackboard (inject manifest when needed).
- Subscribe to live values for source nodes.
- Compose outputs using feature-based selection:
  - Choose best matching Procedure advice.
  - Choose best matching FDIR advice.
  - Combine SOE envelopes by lookup-table intersection when possible.
  - Choose Controller advice, merge limit-only advisors, and apply rate-of-change shaping.
- Publish composed values back to Blackboard via OPC UA updates.

## Runtime Flow
1. Startup:
- Reads `../home/config.json` (creates default file when missing).
- Connects OPC UA client to `Configuration.OPCUAURL`.
- Registers ADCS standard interface query.

2. Per loop tick (`Configuration.LoopDuration`):
- `ManageActivableFunctionList()` discovers and registers new activable functions.
- `ManageControllerFunctionsSetPointsLimitsAndParameters()` composes controller outputs.
- `ManageProcedureParameters()` composes procedure outputs.
- `ManageFaultDetectionIsolationAndRecoveryParameters()` composes FDIR outputs.
- `ManageSafeOperatingEnvelopeParameters()` composes SOE outputs.

3. Publish:
- Writes scalar values and JSON-serialized object payloads to destination Blackboard nodes.

## Project Structure
- `Program.cs`: host bootstrap (`BackgroundService` registration).
- `Worker.cs`: core orchestration, query registration, composition logic, OPC UA writeback.
- `Configuration.cs`: runtime configuration model.
- `Entry.cs`: in-memory query result/live-value store.
- `ControlData.cs`, `ProcedureData.cs`, `FaultDetectionIsolationAndRecoveryFunctionData.cs`, `SafeOperatingEnvelopeFunctionData.cs`: internal correlation/state models.
- `config/Quickstarts.ReferenceClient.Config.xml`: OPC UA client configuration.

## Configuration
Configuration is loaded from:
- `../home/config.json` in local run
- `/home/config.json` in container run (via mounted volume)

Schema:
```json
{
  "LoopDuration": "00:00:01",
  "OPCUAURL": "opc.tcp://localhost:48030",
  "ControllerObsolescence": "00:00:05",
  "ProcedureObsolescence": "00:00:05",
  "FaultDetectionIsolationAndRecoveryObsolescence": "00:00:05",
  "SafeOperatingEnvelopeObsolescence": "00:00:05"
}
```

Notes:
- `LoopDuration`: periodic processing interval.
- `OPCUAURL`: DWIS Blackboard OPC UA endpoint.
- Obsolescence values are present in configuration, but current `Worker` freshness checks effectively use `TimeSpan.MaxValue` constants, so staleness filtering is currently not bounded by those configured values.

## Prerequisites
- .NET 8 SDK (for local build/run)
- Reachable DWIS Blackboard OPC UA endpoint
- Valid OPC UA certificate/trust setup for your environment (see `config/Quickstarts.ReferenceClient.Config.xml`)

## Run Locally
From repository root:
```powershell
dotnet restore .\DWIS.AdviceComposer.sln
dotnet build .\DWIS.AdviceComposer.sln -c Release
dotnet run --project .\DWIS.AdviceComposer.Service\DWIS.AdviceComposer.Service.csproj
```

Ensure `home\config.json` exists at repository root (or let service generate it on first run), then set `OPCUAURL` as needed.

## Build and Run with Docker
Build image from repository root:
```powershell
docker build -f .\DWIS.AdviceComposer.Service\Dockerfile -t dwisadvicecomposerservice:stable .
```

Run container on Windows:
```powershell
docker run -dit --name DWISComposer -v C:\Volumes\DWISAdviceComposerService:/home dwisadvicecomposerservice:stable
```

`C:\Volumes\DWISAdviceComposerService` holds the external `config.json` used by the service.

## Logging
- Uses standard `Microsoft.Extensions.Hosting` logging.
- Default log level is `Information` (`appsettings.json` / `appsettings.Development.json`).
- Service logs include connection/config startup info and send/update events.

## Composition Rules (Current Behavior)
- Feature matching: candidates are grouped by advisor and ranked by overlap with context features.
- Controller selection:
  - Prefer candidates with set-point recommendations.
  - If needed, merge limit recommendations from "limit-only" candidates.
  - Apply max-rate-of-change filtering against the previously sent controller outputs.
- SOE selection:
  - Attempts intersection of multiple SOE lookup tables.
  - For upper bounds, combines by min; for lower bounds, combines by max.
  - Falls back by reducing candidate set until intersection succeeds.

## Troubleshooting
- No outputs written:
  - Verify OPC UA endpoint in `config.json`.
  - Verify activable function payloads are arriving in ADCS standard interface.
  - Confirm source query results resolve and subscriptions receive live values.
- Connection/certificate errors:
  - Check `config/Quickstarts.ReferenceClient.Config.xml` paths and trust stores.
  - Validate certificate permissions for container/local runtime user.
- Wrong/missing advisor selection:
  - Validate context features and advisor feature tags in Blackboard data.
  - Confirm source value freshness and datatype compatibility.
