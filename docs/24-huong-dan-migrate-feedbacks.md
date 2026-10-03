# Việc của Dăm — migrate hai bảng Feedbacks + FeedbackReplies (US 24, Sprint 2)

> Ghi chú này do Phùng Duy Hoàng viết khi làm task **dòng 53 — "API Admin phản hồi và đổi trạng thái
> phản ánh"**. Task **dòng 50 — "Migrate bảng Feedbacks, FeedbackReplies"** (Vàng Thị Dăm) là việc
> còn lại duy nhất để nhánh này chạy được trên CSDL thật.

## Trạng thái hiện tại

Hoàng đã viết sẵn (chỉ để API biên dịch + test được trên InMemory — test tích hợp của dự án chạy
trên EF InMemory nên **không chờ migration**):

| File | Nội dung |
|---|---|
| `backend/SmartBus.Api/Entities/Feedback.cs` | Entity bảng `Feedbacks` — FK `TripId` **nullable** đúng A9 #20 |
| `backend/SmartBus.Api/Entities/FeedbackReply.cs` | Entity bảng `FeedbackReplies` — bảng chỉ ghi thêm, **không** `UpdatedAt` |
| `backend/SmartBus.Api/Entities/FeedbackStatus.cs` | `New` / `InProgress` / `Resolved` |
| `backend/SmartBus.Api/Entities/FeedbackType.cs` | `Complaint` / `Compliment` / `Suggestion` |
| `backend/SmartBus.Api/Data/AppDbContext.Feedback.cs` | DbSet + `ConfigureFeedback` (index, FK Restrict, varchar(20)) |
| `backend/SmartBus.Api/Data/AppDbContext.cs` | đã thêm 2 dòng hook `ConfigureFeedback` — **nhờ Dăm review trong PR** |

Hình dạng bảng khớp hợp đồng **"Phản ánh — /feedbacks"** trong `docs/api-contract.md`.
**Đừng sửa entity/DbContext cho khớp ý mình** — muốn đổi hình dạng thì sửa `api-contract.md` trước
rồi báo Hoàng (⛔5). Việc ở đây chỉ là **sinh migration**.

## Việc cần làm — đúng MỘT lệnh

```bash
cd backend/SmartBus.Api
dotnet ef migrations add Sprint2_Feedbacks_FeedbackReplies
```

(`dotnet ef` chưa cài thì chạy `dotnet tool restore` trước.)

Rồi soát file migration vừa sinh theo checklist dưới, commit trên nhánh riêng + PR như các migration
trước. **Không cần** sửa entity, không cần viết API, không cần đụng `api-contract.md`.

## Checklist soát migration

- [ ] Đủ **2 bảng** tên đúng: `Feedbacks`, `FeedbackReplies` (PascalCase số nhiều — tên bảng lấy từ
      tên DbSet, không phải tên entity).
- [ ] `Feedbacks.TripId` **nullable**; `Feedbacks.UserId` not null.
- [ ] `Type` / `Status` là **`varchar(20)`** — không phải integer (`HasConversion<string>()`, A3).
- [ ] `Content` và `AttachmentUrl` là `text` (không có trần độ dài ở cột).
- [ ] FK nào cũng **`Restrict`**, không có `Cascade` nào: `Feedbacks→Users`, `Feedbacks→Trips`,
      `FeedbackReplies→Feedbacks`, `FeedbackReplies→Users`.
- [ ] Đủ 5 index: `Feedbacks (Status, CreatedAt)`, `Feedbacks (UserId)`, `Feedbacks (TripId)`,
      `FeedbackReplies (FeedbackId, CreatedAt)`, `FeedbackReplies (UserId)`.
- [ ] `CreatedAt` là `timestamptz`; `FeedbackReplies` **không có** `UpdatedAt`; không có cột `IsDeleted`.

## Nhắc nhóm (không phải việc code)

`FeedbackReplies` là bảng thứ **21** — mục A9 của `docs/03-quy-uoc.md` đang liệt kê 20 bảng và chưa
có bảng này. Bảng tính Sprint 2 dòng 50 đã ghi rõ "Migrate bảng Feedbacks, **FeedbackReplies**" nên
hai bảng là chủ trương có sẵn; chỉ cần nhóm chốt bổ sung `FeedbackReplies` vào A9 cho tài liệu khớp
CSDL.
