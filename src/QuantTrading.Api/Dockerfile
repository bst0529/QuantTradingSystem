# 使用 .NET 10 執行環境作為基底
FROM mcr.microsoft.com/dotnet/10.0-alpine AS base
WORKDIR /app
EXPOSE 5050
ENV ASPNETCORE_URLS=http://+:5050

# 使用 .NET 10 SDK 進行編譯
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 複製 csproj 並還原相依性 (利用 Docker 快取加速建置)
COPY ["QuantTrading.Api/QuantTrading.Api.csproj", "QuantTrading.Api/"]
COPY ["QuantTrading.Core/QuantTrading.Core.csproj", "QuantTrading.Core/"]
RUN dotnet restore "QuantTrading.Api/QuantTrading.Api.csproj"

# 複製其餘程式碼並進行編譯
COPY . .
WORKDIR "/src/QuantTrading.Api"
RUN dotnet build "QuantTrading.Api.csproj" -c Release -o /app/build

# 發布應用程式
FROM build AS publish
RUN dotnet publish "QuantTrading.Api.csproj" -c Release -o /app/publish

# 最終映像檔
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 設定時區為台北 (確保時間正確)
ENV TZ=Asia/Taipei
ENTRYPOINT ["dotnet", "QuantTrading.Api.dll"]