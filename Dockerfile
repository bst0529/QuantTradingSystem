# 階段 1：編譯環境 (使用完整的 SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 利用快取機制，先複製 csproj 並還原 NuGet 套件
COPY ["src/QuantTrading.Api/QuantTrading.Api.csproj", "QuantTrading.Api/"]
COPY ["src/QuantTrading.Core/QuantTrading.Core.csproj", "QuantTrading.Core/"]
RUN dotnet restore "QuantTrading.Api/QuantTrading.Api.csproj"

# 複製其餘程式碼並進行 Release 編譯
COPY src/ .
WORKDIR "/src/QuantTrading.Api"
RUN dotnet publish "QuantTrading.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 階段 2：運行環境 (使用極小的 Alpine Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# 🛡️ 資安防護：切換到非特權使用者 (Non-root user)，避免權限提升攻擊
USER app

# 將編譯好的檔案複製到運行環境
COPY --from=build /app/publish .

# 將資料庫檔案也複製到容器的 /app 目錄下
# 請確認路徑正確，如果資料庫在 Api 專案根目錄，請使用：
COPY src/QuantTrading.Api/quant.db .

# 設定環境變數，指示 ASP.NET Core 監聽 8080 port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 啟動應用程式
ENTRYPOINT ["dotnet", "QuantTrading.Api.dll"]