# DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test

## Overview
This project is a test advisor worker that publishes synthetic ROP-related recommendations tagged with the `DrillStemVibrationFeature` capability.

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
- Company: `BakerHughes`
- Advisor: `iTrack`
- Prefix: `DWIS:Advisor:BakerHughes:ROPManagement`
- Manifest name: `ROPAdvisorWithDrillStemVibrationFeature`

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
  "FlowrateAmplitude": 0.006666666666666667,
  "FlowrateAverage": 0.03666666666666667,
  "FlowratePeriod": 42.0,
  "RotationalSpeedAmplitude": 0.3333333333333333,
  "RotationalSpeedAverage": 2.5,
  "RotationalSpeedPeriod": 95.0,
  "ROPAmplitude": 0.0019444444444444444,
  "ROPAverage": 0.006944444444444444,
  "ROPPeriod": 58.0,
  "WOBAmplitude": 3500.0,
  "WOBAverage": 26000.0,
  "WOBPeriod": 74.0,
  "TOBAmplitude": 1500.0,
  "TOBAverage": 18000.0,
  "TOBPeriod": 34.0,
  "DPAmplitude": 700000.0,
  "DPAverage": 1700000.0,
  "DPPeriod": 56.0
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
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj
```

## Build and Run with Docker
Build image:
```powershell
docker build -f .\DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test\Dockerfile -t dwisadvicecomposerropadvisorwithdrillstemvibrationfeaturetest:latest .
```

Run on Windows:
```powershell
docker run --name ropadvisorvibration -v C:\Volumes\DWISAdvisorROPVibrations:/home dwisadvicecomposerropadvisorwithdrillstemvibrationfeaturetest:latest
```

Run on Linux:
```bash
docker run --name ropadvisorvibration -v /home/Volumes/DWISAdvisorROPVibrations:/home dwisadvicecomposerropadvisorwithdrillstemvibrationfeaturetest:latest
```

## Validation Checklist
- Logs show periodic writes: flowrate, rotational speed, ROP, WOB, TOB, dP.
- Blackboard nodes for the six outputs exist (injected or pre-existing).
- When used with AdviceComposer + context scheduler, this advisor should be selected when `DrillStemVibrationFeature` is preferred.

## Troubleshooting
- No values written: verify `OPCUAURL` and certificate/trust configuration in `config/Quickstarts.ReferenceClient.Config.xml`.
- Missing output nodes: ensure manifest injection is allowed and not blocked by server policy.
- Unexpected selection in composition tests: verify scheduler context currently includes `DrillStemVibrationFeature`.
