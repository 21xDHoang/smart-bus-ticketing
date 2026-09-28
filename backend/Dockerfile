# Dockerfile cho SmartBus.Api — dùng để deploy lên Render.
#
# Render KHÔNG có runtime .NET native (xem https://render.com/docs/language-support),
# nên bắt buộc phải đóng gói bằng Docker.
#
# Trên Render đặt: Root Directory = backend, Dockerfile Path = ./Dockerfile
# (đường dẫn COPY bên dưới tính từ thư mục backend/, không phải từ gốc repo).

# ---------- Tầng 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy riêng file .csproj trước rồi mới restore: nhờ vậy sửa code không làm Docker
# chạy lại dotnet restore, build nhanh hơn nhiều ở các lần deploy sau.
COPY SmartBus.Api/SmartBus.Api.csproj SmartBus.Api/
RUN dotnet restore SmartBus.Api/SmartBus.Api.csproj

COPY SmartBus.Api/ SmartBus.Api/
RUN dotnet publish SmartBus.Api/SmartBus.Api.csproj -c Release -o /app/publish

# ---------- Tầng 2: chạy ----------
# Chỉ copy phần đã publish sang image runtime, không mang theo SDK (nhẹ hơn ~700 MB).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render cấp cổng cần lắng nghe qua biến môi trường PORT, mà ASP.NET Core KHÔNG tự
# đọc biến đó — bind cứng vào 5080 là service bị báo unhealthy rồi restart liên tục.
# ${PORT:-8080} để khi chạy thử ở máy vẫn có cổng mặc định.
ENTRYPOINT ["sh", "-c", "dotnet SmartBus.Api.dll --urls http://+:${PORT:-8080}"]
