# ROP Advisor With Cutting Transport Feature
Output every second:
- flowrate setpoint
- rotational speed setpoint
- ROP Max limit
- WOB Max limit
- TOB Max Limit
- dp max limit

Each of these values vary through time using a sinusoidal function, which parameters are taken from the configuration file.

The semantic of each of these values is declared to support the feature `CuttingsTransportFeature`.

## Getting started (internal)
If you have created the docker image yourself, here is the procedured.

The `docker run` command for windows is:
```
docker run --name ropadvisorcuttings -v C:\Volumes\DWISAdvisorROPCuttings:/home dwisadvicecomposerropadvisorwithcuttingstransportfeaturetest:latest
```
where `C:\Volumes\DWISAdvisorROPCuttings` is any folder where you would like to access the config.json file that is used to configure
the application.

and the `docker run` command for linux is:
```
docker run --name ropadvisorcuttings -v /home/Volumes/DWISAdvisorROPCuttings:/home dwisadvicecomposerropadvisorwithcuttingstransportfeaturetest:latest
```
where `/home/Volumes/DWISAdvisorROPCuttings` is any directory where you would like to access the config.json file that is used to
configure the application.

## Configuration
A configuration file is available in the directory/folder that is connected to the internal `/home` directory. The name of the configuration
file is `config.json` and is in Json format.

The configuration file has the following properties:
- `LoopDuration` (a TimeSpan, default 1s): this property defines the loop duration of the service, i.e., the time interval used to check if new signals are available.
- `OPCUAURL` (a string, default "opc.tcp://localhost:48030"): this property defines the `URL` used to connect to the `DWIS Blackboard`
- `FlowrateAmplitude` the flowrate sinusoid amplitude ($m^3/s$)
- `FlowrateAverage` the flowrate average value ($m^3/s$)
- `FlowratePeriod` the flowrate sinusoid period (s)
- `RotationalSpeedAmplitude` the rotational speed sinusoid amplitude (Hz)
- `RotationalSpeedAverage` the rotational average value (Hz)
- `RotationalSpeedPeriod` the rotational sinudois period (s)
- `ROPAmplitude` the rate of penetration sinusoid amplitude (m/s)
- `ROPAverage` the rate of penetration average value (m/s)
- `ROPPeriod` the rate of penetration sinusoid amplitude (s)
- `WOBAmplitude` the weight on bit sinusoid amplitude (kg)
- `WOBAverage` the weight on bit average value (kg)
- `WOBPeriod` the weight on bit sinusoid period (s)
- `TOBAmplitude` the torque on bit sinusoid amplitude (N.m)
- `TOBAverage` the torque on bit average value (N.m)
- `TOBPeriod` the torque on bit sinusoid period (s)
- `DPAmplitude` the PDM differential pressure sinusoid amplitude (Pa)
- `DPAverage` the PDM differential pressure average value (Pa)
- `DPPeriod` the PDM differential pressure sinusoid period (s)
- 