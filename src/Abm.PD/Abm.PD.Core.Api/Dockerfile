FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Abm.PD.Core.Api/Abm.PD.Core.Api.csproj", "Abm.PD.Core.Api/"]
COPY ["Abm.PD.Core.Domain/Abm.PD.Core.Domain.csproj", "Abm.PD.Core.Domain/"]
COPY ["Abm.PD.Core.Repository/Abm.PD.Core.Repository.csproj", "Abm.PD.Core.Repository/"]
RUN dotnet restore "Abm.PD.Core.Api/Abm.PD.Core.Api.csproj"
COPY . .
WORKDIR "/src/Abm.PD.Core.Api"
RUN dotnet build "Abm.PD.Core.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Abm.PD.Core.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Abm.PD.Core.Api.dll"]
