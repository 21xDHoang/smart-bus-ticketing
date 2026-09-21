# Hệ thống bán vé xe buýt thông minh

Đồ án nhóm — ICTU. Backend ASP.NET Core (C#) + Frontend React + TypeScript.

## Thành viên

| Thành viên | Vai trò |
|---|---|
| Phùng Duy Hoàng | Scrum Master + Leader, fullstack |
| Nguyễn Đình Băng | Frontend |
| Dương Thị Hạnh | Frontend |
| Hoàng Văn Thịnh | Frontend |
| Trần Trung Hiếu | Backend |
| Vàng Thị Dăm | Backend |
| Nguyễn Duy Kiên | Backend |
| Giàng A Vàng | Hạ tầng + Kiểm thử |

## Công nghệ

| Tầng | Công nghệ |
|---|---|
| Backend | ASP.NET Core 10, Entity Framework Core, PostgreSQL |
| Xác thực | JWT + BCrypt + RBAC theo vai trò |
| Frontend | React 19, TypeScript, Vite, Ant Design |
| Bản đồ | Leaflet + react-leaflet |
| CSDL | PostgreSQL (Supabase) |
| Kiểm thử | xUnit |

## Cấu trúc thư mục

```
smart-bus-ticketing/
├─ backend/
│  ├─ SmartBus.sln
│  ├─ SmartBus.Api/          # API chính (toàn bộ nghiệp vụ nằm ở đây)
│  └─ SmartBus.Tests/        # Kiểm thử xUnit
├─ frontend/                 # React + Vite + Ant Design
└─ docs/                     # Tài liệu, sơ đồ
```

> Cố ý giữ **một project API duy nhất**, không tách Clean Architecture nhiều tầng.
> Nhóm 8 người làm 5 tuần — tách nhiều project chỉ làm chậm, không giúp gì.

## Yêu cầu môi trường

| Công cụ | Phiên bản | Kiểm tra |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet --version` |
| Node.js | 24.x | `node --version` |
| PostgreSQL | 15+ | hoặc dùng Supabase (không cần cài) |

## Chạy dự án

### Backend

```bash
cd backend
cp SmartBus.Api/appsettings.Development.json.example SmartBus.Api/appsettings.Development.json
# Mở file vừa tạo, điền chuỗi kết nối PostgreSQL và JWT key
dotnet restore
dotnet ef database update --project SmartBus.Api
dotnet run --project SmartBus.Api
```

API chạy ở `http://localhost:5080`, xem Swagger ở `/swagger`.

### Frontend

```bash
cd frontend
cp .env.example .env
npm install
npm run dev
```

Web chạy ở `http://localhost:5173`.

## Quy tắc làm việc nhóm

### Git — 5 quy tắc bắt buộc

1. **Không push thẳng `main`.** Mọi thay đổi đi qua Pull Request và cần 1 người review.
2. Đặt tên nhánh: `feature/<mô-tả-ngắn>` — ví dụ `feature/auth-login`, `feature/route-crud`.
3. Trước khi push: `git pull --rebase origin main`
4. **Chỉ một người được tạo EF Core migration** (hiện tại: Vàng Thị Dăm).
   Lý do: file `ModelSnapshot.cs` thay đổi mỗi lần migrate — 3 người cùng tạo là conflict không gỡ được.
   Ai cần đổi bảng thì nhắn Dăm, hoặc sửa file `AppDbContext.<Domain>.cs` của mình.
5. `AppDbContext` chia thành nhiều file `partial` theo nhóm nghiệp vụ
   (`AppDbContext.Auth.cs`, `AppDbContext.Route.cs`, …) để mỗi người sửa một file riêng.

### Quy trình hằng ngày

```bash
git checkout main
git pull --rebase origin main
git checkout -b feature/ten-task
# ... làm việc ...
git add .
git commit -m "feat(auth): thêm API đăng nhập"
git push -u origin feature/ten-task
# Mở Pull Request trên GitHub, ghi "Closes #<số issue>"
```

### Daily

Mỗi ngày, trả lời 3 câu trên nhóm chat (không cần họp):

1. Hôm qua làm xong gì?
2. Hôm nay làm gì?
3. Đang bị chặn bởi gì?

## Definition of Done

Một task chỉ được đánh dấu `Done` khi:

- [ ] Code đã merge vào `main` qua Pull Request, có 1 người review
- [ ] Chạy được trên môi trường online, không lỗi console
- [ ] Có test cho phần API (nếu là task backend)
- [ ] Cột `Status` trong file `Product_Backlog_Smart_Bus.xlsx` đã chuyển thành `Done`

## Kế hoạch sprint

Xem file `Product_Backlog_Smart_Bus.xlsx` — sheet `Backlogs` (24 user story) và `Sprint 1` … `Sprint 5`.

| Sprint | Nội dung |
|---|---|
| 1 | Phân quyền tài khoản · Quản lý tuyến/trạm/giá vé · Nhật ký hoạt động |
| 2 | Lập lịch trình · Phân công điều xe · Tra cứu tuyến · Vé tháng · Phản ánh |
| 3 | Chọn ghế · Giữ chỗ 10 phút · Thanh toán · Vé QR · Voucher |
| 4 | Hủy & đổi vé · Soát vé QR · Hóa đơn · Hoàn tiền · Duyệt ưu đãi |
| 5 | GPS realtime · Thông báo trạm · Sự cố · Doanh thu · Tỷ lệ lấp đầy · Xuất báo cáo |
