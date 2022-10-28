FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Labo.WcfTestClient.Win.Terminal.vNext/Labo.WcfTestClient.Win.Terminal.vNext.csproj", "Labo.WcfTestClient.Win.Terminal.vNext/"]
RUN dotnet restore "Labo.WcfTestClient.Win.Terminal.vNext/Labo.WcfTestClient.Win.Terminal.vNext.csproj"
COPY . .
WORKDIR "/src/Labo.WcfTestClient.Win.Terminal.vNext"
RUN dotnet build "Labo.WcfTestClient.Win.Terminal.vNext.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Labo.WcfTestClient.Win.Terminal.vNext.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Labo.WcfTestClient.Win.Terminal.vNext.dll"]
