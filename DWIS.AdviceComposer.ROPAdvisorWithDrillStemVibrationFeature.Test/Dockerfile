# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

RUN mkdir -p /app/config
RUN chmod -R 755 /app/config

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/config/Quickstarts.ReferenceClient.Config.xml", "/app/config/"]
COPY ["DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj", "DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/"]
RUN dotnet restore "DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test/DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj"
COPY . .
WORKDIR "/src/DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test"
RUN dotnet build "DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
VOLUME /home
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test.dll"]