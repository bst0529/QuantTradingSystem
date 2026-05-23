# 階段 1：編譯環境
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 補上 src/ 前綴路徑
COPY ["src/QuantTrading.Worker/QuantTrading.Worker.csproj", "QuantTrading.Worker/"]
COPY ["src/QuantTrading.Core/QuantTrading.Core.csproj", "QuantTrading.Core/"]
RUN dotnet restore "QuantTrading.Worker/QuantTrading.Worker.csproj"

# 只複製 Worker 與 Core 的程式碼
COPY src/QuantTrading.Worker/ QuantTrading.Worker/
COPY src/QuantTrading.Core/ QuantTrading.Core/

WORKDIR "/src/QuantTrading.Worker"
RUN dotnet publish "QuantTrading.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 階段 2：運行環境
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# 建立共用磁碟區並賦予權限
RUN mkdir /app/data && chown -R app:app /app/data
USER app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QuantTrading.Worker.dll"]