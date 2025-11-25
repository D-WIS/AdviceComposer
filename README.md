# AdviceComposer

.NET 8 service and supporting simulators that compose multiple DWIS/ADCS advices into unified controller/procedure parameters, limits (SOE), and FDIR inputs over the DWIS OPC UA blackboard.

## Repository layout
- `DWIS.AdviceComposer.sln` — solution for service, model, and advisor simulators.
- `DWIS.AdviceComposer.Service/` — background service that reads advisory inputs from the DWIS blackboard, reconciles controller/procedure/SOE/FDIR parameters, and publishes composed outputs. Dockerfile and config template included.
- `DWIS.AdviceComposer.Model/` — shared types (procedure/controller data) and helpers (ADCS standard interface SparQL manifest loader).
- Advisor simulators (generate sinusoidal setpoints/limits with feature semantics):
  - `DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test/`
  - `DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/`
  - `DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test/`
- Context driver: `DWIS.AdviceComposer.SchedulerROPContext.Test/` alternates AutoDriller feature contexts (rig action plan vs cuttings/vibration) on a configurable period.
- `home/config.json` — sample service configuration (loop duration and OPC UA URL).
- `.github/workflows/` — build/pack model and build/push interpreter images.

## Build
- `dotnet build DWIS.AdviceComposer.sln`

## Run (service)
- Local: `dotnet run --project DWIS.AdviceComposer.Service` (ensure `config.json` is available/mounted at `/home`).
- Docker (stable image):
  - Windows example: `docker run -d --name advicecomposer -v C:\Volumes\DWISAdviceComposerService:/home digiwells/dwisadvicecomposerservice:stable`
  - Linux example: `docker run -d --name advicecomposer -v /home/Volumes/DWISAdviceComposerService:/home digiwells/dwisadvicecomposerservice:stable`

## Configuration (service)
`/home/config.json` keys observed:
- `LoopDuration` (TimeSpan, default 1s)
- `OPCUAURL` (DWIS blackboard endpoint, e.g., `opc.tcp://localhost:48030`)
Advisor simulators include their own richer configs (sinusoid amplitudes/averages/periods for flowrate, RPM, ROP, WOB, TOB, DP; context change periods).

## Notes
- Advisor simulators are standalone containers; mount `/home` to supply their configs. They publish feature-tagged signals (CuttingsTransport, DrillStemVibration, RigActionPlan) for composition testing.
- Service uses DWIS OPC UA client (`DWISClientOPCF`) and enforces capability preferences/locking via ADCS standard interface helpers.