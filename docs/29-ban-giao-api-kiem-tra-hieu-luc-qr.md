# Bàn giao — "API kiểm tra hiệu lực vé theo mã QR" (dòng 43, US 4, Sprint 3)

> Viết bởi **Vàng Thị Dăm** (Chủ CSDL). Đây là **bàn giao**, không phải bản cài đặt — Dăm không viết
> Controller/Service (xem §1). Người nhận: **Trần Trung Hiếu** (Chủ API).
>
> Tài liệu này **không** thay `api-contract.md`. Hình dạng endpoint dưới đây là **đề xuất**: Hiếu chốt,
> sửa `api-contract.md`, rồi mới code (luật 5).
>
> ⚠️ `docs/28-csdl-ve-dien-tu.md`, `Entities/Ticket.cs` và `Data/AppDbContext.Ticket.cs` mà tài liệu này
> viện dẫn thuộc **task dòng 38** (nhánh `feature/4-migrate-tickets-ticketqrcodes`) — chưa có trên
> `main` cho tới khi PR của nhánh đó được merge.

## 1. Vì sao Dăm không tự code

Bảng phân công `Product_Backlog_Smart_Bus.xlsx` **sheet Sprint 3, dòng 43** ghi:

> Task: *"API kiểm tra hiệu lực vé theo mã QR"* · Ưu tiên Cao · Estimate 4 · Assign **Vàng Thị Dăm**

Nhưng `quy uoc.docx` §2.4, mục **Chủ CSDL — Vàng Thị Dăm**, cột ⛔ *Không được làm*, ghi:

> **Viết Controller / Service nghiệp vụ** · Sửa file `AppDbContext.<NghiệpVụ>.cs` của người khác

Hai tài liệu trái nhau. §0.5 nói ca này phải **báo người dùng, đừng tự chọn một cái** — Dăm đã báo và
được chốt là **theo §2.4**: không viết API, chỉ bàn giao. Ba bằng chứng cho thấy dòng 43 **lệch phân
công**, không phải quy ước sai:

| # | Bằng chứng |
|---|---|
| 1 | **Cùng một endpoint đã giao cho Hiếu ở Sprint 4.** Sheet Sprint 4 dòng 15 (US 15 "Soát vé QR"): *"API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* — **Trần Trung Hiếu**, Cao, 6. Dòng 43 và dòng 15 mô tả **cùng một việc**; bản Sprint 4 chỉ giàu hơn ở chỗ có xác thực chữ ký (vì dòng 39 mới sinh ra chữ ký ở Sprint 3) |
| 2 | **Mọi dòng "API …" khác của Sprint 3 đều thuộc Hiếu hoặc Kiên** — dòng 43 là ngoại lệ duy nhất rơi vào người phụ trách CSDL. Tiền lệ: *"API kiểm tra và áp dụng voucher"* (dòng 52) giao Kiên, và Kiên đã làm `VoucherValidationController.cs` |
| 3 | **Hai tài liệu phân công của nhóm vốn đã lệch nhau và chưa chốt lại.** Mục J của `quy uoc.docx` liệt "Xung đột #7 — phân công lệch giữa hai tài liệu" và ghi rõ: *"Phần 2.4 ở trên vì vậy chỉ quy định ranh giới theo vai trò, không khẳng định ai làm task nào."* |

→ **Đề nghị nhóm: chuyển dòng 43 cho Hiếu** và sửa lại sheet. Tin nhắn soạn sẵn ở §6.

## 2. Phần CSDL đã xong — không cần đổi bảng nào

Endpoint này **không cần thêm bảng, không cần sửa cột**. Những gì nó cần đã nằm trong bảng `Tickets`
(`docs/28-csdl-ve-dien-tu.md`):

| Cần gì | Đã có |
|---|---|
| Tra vé theo nội dung mã QR | `Tickets.Code` — `varchar(200)`, **có chỉ mục UNIQUE** (`IX_Tickets_Code`) → tra một dòng theo mã, không quét bảng |
| Biết vé còn dùng được không | `Tickets.Status` — `varchar(20)`, ba giá trị `Paid` / `Used` / `Cancelled` |
| Biết vé đã bị soát lúc nào | `Tickets.UsedAt` — nullable, chỉ khác null khi `Status = 'Used'` |
| Suy "vé hết hạn" (chuyến đã chạy) | `Tickets.TripId` → `Trips` |
| Hiển thị kết quả cho phụ xe | `SeatId` → `Seats`, `BoardingStopId`/`AlightingStopId` → `Stops` |

⚠️ **`Code` dài tới 200 ký tự là cố ý** — chừa chỗ cho mã QR **có ký số** của Kiên (dòng 39). Request
của endpoint phải nhận tối thiểu 200 ký tự, **đừng** khai `HasMaxLength(36)` theo `Guid`.

⚠️ **`TicketValidations` KHÔNG thuộc dòng 43.** Bảng đó là **Sprint 4 dòng 14** (US 15, cũng của Dăm).
Đừng gộp hai việc.

## 3. Đề xuất hợp đồng endpoint — Hiếu chốt

Theo lối đã dùng ở `POST /vouchers/validate`:

```
POST /api/tickets/validate
Authorization: Bearer <JWT>          ← vai trò lái xe/phụ xe (Sprint 4 dòng 19: "chỉ tài xế/phụ xe được soát vé")
Body:  { "code": "TV-01-8F3A2C...", "tripId": "…uuid…" }
```

| Trường request | Kiểu | Ghi chú |
|---|---|---|
| `code` | string, ≤ 200 | Nội dung mã QR quét được. Bắt buộc |
| `tripId` | string (GUID), nullable | Chuyến đang chạy của phụ xe — xem câu hỏi (3) ở §4 |

```jsonc
// 200 — kiểm tra CHẠY XONG. Hợp lệ hay không đều là 200, không phải 4xx.
{ "valid": true,  "reason": null,          "ticket": { /* hình dạng Ticket như mục /tickets */ } }
{ "valid": false, "reason": "AlreadyUsed", "ticket": { /* … */ } }
```

| `reason` | Nghĩa |
|---|---|
| `null` | Vé hợp lệ, được lên xe |
| `NotFound` | Không có vé nào mang mã này |
| `Cancelled` | `Status = 'Cancelled'` |
| `AlreadyUsed` | `Status = 'Used'` (kèm `usedAt` để phụ xe đối chiếu) |
| `WrongTrip` | Vé đúng nhưng của chuyến khác |
| `Expired` | Chuyến đã khởi hành xong mà vé chưa được soát |

**Vì sao "vé không hợp lệ" trả 200 chứ không 409:** đây là **câu trả lời**, không phải lỗi — cùng lối
`POST /vouchers/validate` trả `valid: false` kèm lý do. Định dạng lỗi D3 (`{message, errors}`) chỉ dành
cho request sai, thiếu quyền, hoặc mã quá dài. **Hiếu chốt lại** — nếu chọn khác thì sửa
`api-contract.md` trước.

## 4. Bốn câu hỏi Hiếu phải chốt — Dăm không tự quyết

1. **Đường dẫn.** `POST /tickets/validate` (đối xứng `/vouchers/validate`) hay `GET /tickets/by-code/{code}`?
   ⚠️ Nếu dùng `/tickets/{code}` thì đụng `GET /tickets/{id}` đã có trong contract — **đừng** dùng.
2. **Chỉ đọc, hay có ghi?** Dòng 43 nói *"kiểm tra"* — chỉ đọc. Nhưng nếu endpoint này **đánh dấu
   `Paid` → `Used`** thì nó chồng lên **Sprint 4 dòng 16** (*"API chống soát trùng: đánh dấu vé đã sử
   dụng theo chuyến"* — Kiên), và lúc đó cần chống hai lượt soát song song. Bảng `Tickets` hiện
   **chưa có `xmin`** (cố ý — `docs/28` §1 ghi lý do). Muốn thêm thì **báo Dăm trước**, không tốn
   migration cột vì `xmin` là cột hệ thống, nhưng phải sửa `AppDbContext.Ticket.cs`.
3. **Có ràng buộc chuyến không?** *"Soát vé theo chuyến"* (Sprint 4 dòng 16) gợi ý phải khớp chuyến
   đang chạy → request cần `tripId` và trả `WrongTrip`. Nếu chỉ tra mã thì bỏ `tripId`.
4. **Vé tháng có dùng chung endpoint này không?** `MonthlyPasses.Code` **cũng** là mã QR soát vé (A6 ghi
   rõ cho cả hai bảng: *"mã QR soát vé"*). Nếu phụ xe quét một mã mà không biết trước là vé lượt hay vé
   tháng, endpoint phải tra **hai** bảng — ảnh hưởng thiết kế, không phải chi tiết nhỏ.

## 5. Phụ thuộc chưa xong tại thời điểm viết

| Việc | Ai | Trạng thái |
|---|---|---|
| Dòng 39 — Service sinh mã QR duy nhất + **ký số chống làm giả** | Nguyễn Duy Kiên | Chưa có file nào trong `Services/`. Vì vậy *"xác thực chữ ký"* (Sprint 4 dòng 15) **chưa làm được**; bản Sprint 3 chỉ còn tra bảng |
| Dòng 38 — Migrate bảng `Tickets` | Vàng Thị Dăm | Code entity/EF **xong**; lệnh `dotnet ef migrations add` chưa chạy tại thời điểm viết |
| Dòng 43 — API kiểm tra hiệu lực | **chờ chuyển cho Hiếu** | Xem §1 |
| Dòng 48 — Test API phát hành vé và kiểm tra hiệu lực QR (xUnit) | Giàng A Vàng | Chờ endpoint |

## 6. Tin nhắn gửi nhóm (copy-paste)

> Mọi người ơi, mình rà lại bảng phân công thì thấy **sheet Sprint 3 dòng 43** giao cho Dăm task
> *"API kiểm tra hiệu lực vé theo mã QR"*, nhưng mục **2.4** của `quy uoc.docx` ghi Chủ CSDL ⛔ không
> viết Controller/Service. Mình không tự quyết, báo nhóm trước.
>
> Hai chỗ cho thấy dòng này bị lệch phân công:
> 1. **Sheet Sprint 4 dòng 15** (US 15) đã giao đúng việc đó cho **Hiếu**: *"API soát vé: nhận mã QR,
>    xác thực chữ ký, trả kết quả hợp lệ/không"*. Dòng 43 và dòng 15 là cùng một endpoint.
> 2. Mọi dòng "API …" khác của Sprint 3 đều của Hiếu hoặc Kiên — dòng 43 là ngoại lệ duy nhất.
>
> Đề nghị: **chuyển dòng 43 cho Hiếu**, Dăm bàn giao phần CSDL. Mình đã viết sẵn đề xuất hợp đồng
> endpoint + 4 câu hỏi cần Hiếu chốt ở `docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md`. Ai phản đối thì
> nói trước khi Hiếu bắt tay vào code nhé.

## 7. Việc của Dăm sau tài liệu này

1. Chạy `dotnet ef migrations add Sprint3_Payments_Vouchers_Tickets` (luật 3 — chỉ Dăm chạy) và soát
   file migration theo checklist trong `docs/28` §4.
2. Đẩy nhánh `feature/4-migrate-tickets-ticketqrcodes` lên và mở PR.
3. **Sprint 4 dòng 14** — `Migrate bảng TicketValidations` (US 15) là việc tiếp theo của Dăm, **khác**
   dòng 43. Chỉ làm khi tới Sprint 4.
