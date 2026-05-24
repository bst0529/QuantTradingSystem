# 階段 1：編譯環境
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 先複製 csproj 並還原
COPY ["src/QuantTrading.Api/QuantTrading.Api.csproj", "QuantTrading.Api/"]
COPY ["src/QuantTrading.Core/QuantTrading.Core.csproj", "QuantTrading.Core/"]
RUN dotnet restore "QuantTrading.Api/QuantTrading.Api.csproj"

# 只複製 API 與 Core，完全避開 Worker 目錄以免衝突
COPY src/QuantTrading.Api/ QuantTrading.Api/
COPY src/QuantTrading.Core/ QuantTrading.Core/

WORKDIR "/src/QuantTrading.Api"
RUN dotnet publish "QuantTrading.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 階段 2：運行環境
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# 建立共用磁碟區並賦予權限
RUN mkdir /app/data && chown -R app:app /app/data
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuantTrading.Api.dll"]