# DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test

## Overview
This project is a test advisor worker that publishes synthetic ROP-related limit recommendations tagged with the `RigActionPlanFeature` capability.

It is intended to be used with:
- `DWIS.AdviceComposer.Service` (composition logic)
- `DWIS.AdviceComposer.SchedulerROPContext.Test` (context switching)

## What It Publishes
At each loop tick (`LoopDuration`), the worker writes sinusoidal test signals to Blackboard outputs resolved from semantic queries/manifests:
- `BOSFlowrateAdvisedMaximum` (max limit)
- `BOSRotationalSpeedAdvisedMaximum` (max limit)
- `ROPAdvisedMaximum` (max limit)
- `WOBAdvisedMaximum` (max limit)
- `BitTorqueAdvisedMaximum` (max limit)
- `DifferentialPressureAdvisedMaximum` (max limit)

Signal values are generated from:
- `value = average + amplitude * sin(2*pi*t/period)`

## Advisor Identity Used in Manifest Injection
When destination nodes are injected/resolved, advisor identity is rewritten to:
- Company: `Halliburton`
- Advisor: `AkerBPHalliburtonAdvisor`
- Prefix: `DWIS:Advisor:Halliburton:ROPManagement`
- Manifest name: `ROPAdvisorWithRigActionPlanFeature`

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
  "FlowrateAmplitude": 0.008666666666666666,
  "FlowrateAverage": 0.03166666666666667,
  "FlowratePeriod": 56.0,
  "RotationalSpeedAmplitude": 0.38333333333333336,
  "RotationalSpeedAverage": 2.6666666666666665,
  "RotationalSpeedPeriod": 85.0,
  "ROPAmplitude": 0.0011111111111111111,
  "ROPAverage": 0.007222222222222222,
  "ROPPeriod": 84.0,
  "WOBAmplitude": 2600.0,
  "WOBAverage": 16000.0,
  "WOBPeriod": 45.0,
  "TOBAmplitude": 1800.0,
  "TOBAverage": 15000.0,
  "TOBPeriod": 56.0,
  "DPAmplitude": 400000.0,
  "DPAverage": 1500000.0,
  "DPPeriod": 27.0
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
dotnet run --project .\DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test\DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test.csproj
```

## Build and Run with Docker
Build image:
```powershell
docker build -f .\DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test\Dockerfile -t dwisadvicecomposerropadvisorwithrigactionplanfeaturetest:latest .
```

Run on Windows:
```powershell
docker run --name ropadvisorrap -v C:\Volumes\DWISAdvisorROPRAP:/home dwisadvicecomposerropadvisorwithrigactionplanfeaturetest:latest
```

Run on Linux:
```bash
docker run --name ropadvisorrap -v /home/Volumes/DWISAdvisorROPRAP:/home dwisadvicecomposerropadvisorwithrigactionplanfeaturetest:latest
```

## Validation Checklist
- Logs show periodic writes: flowrate, rotational speed, ROP, WOB, TOB, dP.
- Blackboard nodes for the six outputs exist (injected or pre-existing).
- In composition tests, this advisor contributes `RigActionPlanFeature` limits and is typically combined with another feature-specific advisor.

## Troubleshooting
- No values written: verify `OPCUAURL` and certificate/trust configuration in `config/Quickstarts.ReferenceClient.Config.xml`.
- Missing output nodes: ensure manifest injection is allowed and not blocked by server policy.
- Unexpected selection in composition tests: verify scheduler context still includes `RigActionPlanFeature`.
