FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY ["QuantTrading.Worker/QuantTrading.Worker.csproj", "QuantTrading.Worker/"]
COPY ["QuantTrading.Core/QuantTrading.Core.csproj", "QuantTrading.Core/"]
RUN dotnet restore "QuantTrading.Worker/QuantTrading.Worker.csproj"

COPY . .
WORKDIR "/src/QuantTrading.Worker"
RUN dotnet build "QuantTrading.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "QuantTrading.Worker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 設定時區為台北
ENV TZ=Asia/Taipei
ENTRYPOINT ["dotnet", "QuantTrading.Worker.dll"]