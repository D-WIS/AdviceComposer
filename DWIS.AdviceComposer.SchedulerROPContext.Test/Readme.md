# Simulation of the Scheduler Management of Context for the Auto-driller ADCS Standard Function
Alternates regularly the context for the ADCS Standard Auto-driller function from:
- RigActionPlanFeature, CuttingsTransportFeature
- RigActionPlanFeature, DrillStemVibrationFeature

The period of alternance is defined in `ContextChangePeriod` in the `config.json` file.

## Getting started (internal)
If you have created the docker image yourself, here is the procedured.

The `docker run` command for windows is:
```
docker run --name schedulerropcontext -v C:\Volumes\DWISSchedulerROPContext:/home dwisadvicecomposerschedulerropcontexttest:latest
```
where `C:\Volumes\DWISSchedulerROPContext` is any folder where you would like to access the config.json file that is used to configure
the application.

and the `docker run` command for linux is:
```
docker run --name schedulerropcontext -v /home/Volumes/DWISSchedulerROPContext:/home dwisadvicecomposerschedulerropcontexttest:latest
```
where `/home/Volumes/DWISSchedulerROPContext` is any directory where you would like to access the config.json file that is used to
configure the application.

## Configuration
A configuration file is available in the directory/folder that is connected to the internal `/home` directory. The name of the configuration
file is `config.json` and is in Json format.

The configuration file has the following properties:
- `LoopDuration` (a TimeSpan, default 1s): this property defines the loop duration of the service, i.e., the time interval used to check if new signals are available.
- `OPCUAURL` (a string, default "opc.tcp://localhost:48030"): this property defines the `URL` used to connect to the `DWIS Blackboard`
- `ContextChangePeriod` period for alternating between the two contexts (s).