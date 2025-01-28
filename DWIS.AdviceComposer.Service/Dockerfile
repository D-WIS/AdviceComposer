# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

RUN mkdir -p /app/config
RUN chmod -R 755 /app/config

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["DWIS.AdviceComposer.Service/config/Quickstarts.ReferenceClient.Config.xml", "/app/config/"]
COPY ["DWIS.AdviceComposer.Model/DWIS.AdviceComposer.Model.csproj", "DWIS.AdviceComposer.Model/"]
COPY ["DWIS.AdviceComposer.Service/DWIS.AdviceComposer.Service.csproj", "DWIS.AdviceComposer.Service/"]
RUN dotnet restore "DWIS.AdviceComposer.Service/DWIS.AdviceComposer.Service.csproj"
COPY . .
WORKDIR "/src/DWIS.AdviceComposer.Service"
RUN dotnet build "DWIS.AdviceComposer.Service.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "DWIS.AdviceComposer.Service.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
VOLUME /home
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "DWIS.AdviceComposer.Service.dll"]
