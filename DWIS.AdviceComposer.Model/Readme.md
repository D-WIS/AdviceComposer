# DWIS.AdviceComposer.Model

## Overview
`DWIS.AdviceComposer.Model` is the shared model library for Advice Composer. It provides the data contracts used to collect, compose, and route advisor outputs for Controller, Procedure, FDIR, and SOE capabilities.

This project is referenced by `DWIS.AdviceComposer.Service` and keeps composition payload structures separate from orchestration logic.

## Target Framework
- `net8.0`

## Package Dependencies
- `DWIS.RigOS.Capabilities.Controller.Model`
- `DWIS.RigOS.Capabilities.Procedure.Model`
- `DWIS.RigOS.Capabilities.FDIR.Model`
- `DWIS.RigOS.Capabilities.SOE.Model`

## Key Types

### `ADCSStandardInterfaceHelper`
Defines semantic metadata attributes for the ADCS standard function signal and generates the SPARQL query used to discover activable functions.

Main outputs:
- `SparQLQuery`
- `SparQLVariables`

### `ControllerFunctionData`
Container for controller advice candidates grouped per advisor.

Fields include:
- `Features`: context feature tags associated with the advisor output.
- `AdvisorName`: advisor identity.
- `Parameters`: top-level controller-function parameters payload.
- `ParametersDestinationQueryResult`: resolved destination node(s) for writing parameters.
- `ControllerDatas`: list of controller-level recommendations.

Related nested types:
- `ControllerData`: per-controller payload with parameter recommendation, set-point recommendation, measured value, rate-of-change, and destinations.
- `ControllerLimitData`: per-limit recommendation with rate-of-change, destination, and `IsMin` flag.

### `ProcedureData`
Advisor candidate payload for procedure recommendations:
- `Features`
- `AdvisorName`
- `Parameters`
- `ParametersDestinationQueryResult`

### `FaultDetectionIsolationAndRecoveryData`
Advisor candidate payload for FDIR recommendations:
- `Features`
- `AdvisorName`
- `Parameters`
- `ParametersDestinationQueryResult`

### `SafeOperatingEnvelopeData`
Advisor candidate payload for SOE recommendations:
- `Features`
- `AdvisorName`
- `Parameters`
- `ParametersDestinationQueryResult`

## Design Notes
- Models intentionally keep payloads flexible with `object? Parameters`, because parameter types vary by capability and advisor implementation.
- `QueryResult` references are embedded so orchestration code can write directly to resolved Blackboard destination variables.
- Feature tags (`Vocabulary.Schemas.Nouns.Enum`) support context-based candidate selection and composition.

## Typical Usage
In `DWIS.AdviceComposer.Service`, these models are used to:
1. Gather advisor candidates from live Blackboard sources.
2. Annotate candidates with context features.
3. Select and/or combine the best candidate(s).
4. Publish resulting parameters, set-points, and limits to destination nodes.

## Build
From repository root:
```powershell
dotnet restore .\DWIS.AdviceComposer.sln
dotnet build .\DWIS.AdviceComposer.Model\DWIS.AdviceComposer.Model.csproj -c Release
```
