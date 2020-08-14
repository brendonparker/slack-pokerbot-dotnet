FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-stage
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c "Release" --framework "netcoreapp3.1" /p:GenerateRuntimeConfigurationFiles=true --runtime linux-x64 --self-contained false -p:PublishReadyToRun=true

FROM scratch AS export-stage
COPY --from=build-stage /app/bin/Release/netcoreapp3.1/linux-x64 /
