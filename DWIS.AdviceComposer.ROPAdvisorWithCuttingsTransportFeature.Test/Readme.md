# DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test

## Overview
This project is a test advisor worker that publishes synthetic ROP-related recommendations tagged with the `CuttingsTransportFeature` capability.

It is intended to be used with:
- `DWIS.AdviceComposer.Service` (composition logic)
- `DWIS.AdviceComposer.SchedulerROPContext.Test` (context switching)

## What It Publishes
At each loop tick (`LoopDuration`), the worker writes sinusoidal test signals to Blackboard outputs resolved from semantic queries/manifests:
- `BOSFlowrateAdvisedTarget` (set-point)
- `BOSRotationalSpeedAdvisedTarget` (set-point)
- `ROPAdvisedMaximum` (max limit)
- `WOBAdvisedMaximum` (max limit)
- `BitTorqueAdvisedMaximum` (max limit)
- `DifferentialPressureAdvisedMaximum` (max limit)

Signal values are generated from:
- `value = average + amplitude * sin(2*pi*t/period)`

## Advisor Identity Used in Manifest Injection
When destination nodes are injected/resolved, advisor identity is rewritten to:
- Company: `Sekal`
- Advisor: `DrillTronics`
- Prefix: `DWIS:Advisor:Sekal:ROPManagement:`
- Manifest name: `ROPAdvisorWithCuttingsTransportFeature`

## Runtime Behavior
1. Connects to OPC UA Blackboard (`OPCUAURL`).
2. Subscribes to ADCS standard auto-driller function descriptions.
3. Resolves or injects destination variables for all published outputs.
4. Periodically publishes the synthetic recommendations.

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
  "FlowrateAmplitude": 0.008333333333333333,
  "FlowrateAverage": 0.03333333333333333,
  "FlowratePeriod": 60.0,
  "RotationalSpeedAmplitude": 0.5,
  "RotationalSpeedAverage": 2.0,
  "RotationalSpeedPeriod": 100.0,
  "ROPAmplitude": 0.002777777777777778,
  "ROPAverage": 0.008333333333333333,
  "ROPPeriod": 45.0,
  "WOBAmplitude": 5000.0,
  "WOBAverage": 30000.0,
  "WOBPeriod": 66.0,
  "TOBAmplitude": 1000.0,
  "TOBAverage": 20000.0,
  "TOBPeriod": 22.0,
  "DPAmplitude": 500000.0,
  "DPAverage": 2000000.0,
  "DPPeriod": 52.0
}
```

Parameter meaning:
- `LoopDuration`: publish interval.
- `OPCUAURL`: OPC UA server endpoint.
- For each signal: `Amplitude`, `Average`, `Period` define the sinusoid.

## Run Locally
From repository root:
```powershell
dotnet restore .\DWIS.AdviceComposer.sln
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test.csproj
```

## Build and Run with Docker
Build image:
```powershell
docker build -f .\DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test\Dockerfile -t dwisadvicecomposerropadvisorwithcuttingstransportfeaturetest:latest .
```

Run on Windows:
```powershell
docker run --name ropadvisorcuttings -v C:\Volumes\DWISAdvisorROPCuttings:/home dwisadvicecomposerropadvisorwithcuttingstransportfeaturetest:latest
```

Run on Linux:
```bash
docker run --name ropadvisorcuttings -v /home/Volumes/DWISAdvisorROPCuttings:/home dwisadvicecomposerropadvisorwithcuttingstransportfeaturetest:latest
```

## Validation Checklist
- Logs show periodic writes: flowrate, rotational speed, ROP, WOB, TOB, dP.
- Blackboard nodes for the six outputs exist (injected or pre-existing).
- When used with AdviceComposer + context scheduler, this advisor should be selected when `CuttingsTransportFeature` is preferred.

## Troubleshooting
- No values written: verify `OPCUAURL` and certificate/trust configuration in `config/Quickstarts.ReferenceClient.Config.xml`.
- Missing output nodes: ensure manifest injection is allowed and not blocked by server policy.
- Unexpected selection in composition tests: verify scheduler context currently includes `CuttingsTransportFeature`.
