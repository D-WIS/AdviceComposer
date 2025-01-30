# AdviceComposer
Perform the composition of multiple advices

## Getting started (external)
If you wish to install the docker image from dockerhub, here is the procedure.

The `docker run` command for windows is:
```
docker run --name advicecomposer -v C:\Volumes\DWISAdviceComposerService:/home digiwells/dwisadvicecomposerservice:stable
```
where `C:\Volumes\DWISAdviceComposerService` is any folder where you would like to access the config.json file that is used to configure
the application.

and the `docker run` command for linux is:
```
docker run --name advicecomposer -v /home/Volumes/DWISAdviceComposerService:/home digiwells/dwisadvicecomposerservice:stable
```
where `/home/Volumes/DWISAdviceComposerService` is any directory where you would like to access the config.json file that is used to
configure the application.

## Getting started (internal)
If you have created the docker image yourself, here is the procedured.

The `docker run` command for windows is:
```
docker run --name advicecomposer -v C:\Volumes\DWISAdviceComposerService:/home dwisadvicecomposerservice:latest
```
where `C:\Volumes\DWISAdviceComposerService` is any folder where you would like to access the config.json file that is used to configure
the application.

and the `docker run` command for linux is:
```
docker run --name advicecomposer -v /home/Volumes/DWISAdviceComposerService:/home dwisadvicecomposerservice:latest
```
where `/home/Volumes/DWISAdviceComposerService` is any directory where you would like to access the config.json file that is used to
configure the application.

## Configuration
A configuration file is available in the directory/folder that is connected to the internal `/home` directory. The name of the configuration
file is `config.json` and is in Json format.

The configuration file has the following properties:
- `LoopDuration` (a TimeSpan, default 1s): this property defines the loop duration of the service, i.e., the time interval used to check if new signals are available.
- `OPCUAURL` (a string, default "opc.tcp://localhost:48030"): this property defines the `URL` used to connect to the `DWIS Blackboard`

