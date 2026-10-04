<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **smart-bus-ticketing** (358 symbols, 652 relationships, 13 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/smart-bus-ticketing/context` | Codebase overview, check index freshness |
| `gitnexus://repo/smart-bus-ticketing/clusters` | All functional areas |
| `gitnexus://repo/smart-bus-ticketing/processes` | All execution flows |
| `gitnexus://repo/smart-bus-ticketing/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

<!-- quy-uoc-nhom:start -->
# Quy ước nhóm — Smart Bus Ticketing

> **Bản tóm tắt luật cốt lõi**, tự động nạp mỗi session. Bản đầy đủ: **`docs/03-quy-uoc.md`** — mở ra khi cần tra chi tiết (bảng kiểu dữ liệu, 20 bảng, xử lý lỗi Git, Definition of Ready/Done…).
>
> ⚠️ File này và **`AGENTS.md` phải giống hệt nhau**. Sửa một file thì sửa cả hai.

## 1. Tám điều tuyệt đối không vi phạm

1. **Không push thẳng lên `main`.** Mọi thay đổi đi qua nhánh riêng → Pull Request → **Squash and merge**.
2. **Không commit bí mật.** Repo **PUBLIC**. Không API key / mật khẩu / connection string trong code — dùng `dotnet user-secrets` (máy) hoặc Environment Variables (deploy). Lỡ commit thì **đổi key ngay**, xoá file không cứu được.
3. **Không tự chạy `dotnet ef migrations add`.** `Migrations/*` và `ModelSnapshot.cs` thuộc riêng **Vàng Thị Dăm**. Cần đổi bảng → nhắn Dăm, không tự sửa.
4. **Không sửa file của người khác.**

| File chỉ một người được sửa | Người sở hữu |
|---|---|
| `Program.cs`, `SmartBus.Api.csproj`, `.gitignore`, `global.json`, `.editorconfig`, `.vscode/`, `.github/` | Phùng Duy Hoàng |
| `frontend/package.json`, `frontend/src/main.tsx`, `App.tsx` | Nguyễn Đình Băng |
| `Migrations/*`, `ModelSnapshot.cs` | Vàng Thị Dăm |

5. **Không tự đổi hình dạng API.** Muốn đổi endpoint/field → sửa `api-contract.md` trước → báo người còn lại → rồi mới code.
6. **Không thêm, không sửa User Story.** Đúng 24 story, 5 sprint.
7. **Không tự merge Pull Request của chính mình.** Phải có 1 người khác approve.
8. **Không `git push --force` / `git reset --hard`.** Hai lệnh này xoá code của người khác không cứu được.

`AppDbContext` được chia thành nhiều file `partial` theo nghiệp vụ — **chỉ sửa file phần của mình**. Xem `docs/01-kien-truc.md`.

**Ranh giới đầy đủ theo từng vai trò** (ai được làm gì, không được làm gì, hỏi ai khi kẹt): `docs/03-quy-uoc.md` **PHẦN 2**. Tám thành viên: Hoàng (Scrum Master, hạ tầng/CI), Dăm (Chủ CSDL, migrations), Hiếu (Chủ API), Băng (Chủ giao diện), Thịnh/Hạnh/Vàng (frontend, Vàng thêm kiểm thử), Kiên (backend).

## 2. Phạm vi — đóng băng

- Đúng **24 User Story**. **Không thêm, không sửa** story nào.
- Chỉ **5 sprint**. **Không mở Sprint 6.**
- **Không đụng sprint đang chạy** — mọi thay đổi lộ trình phải đẩy sang sprint kế tiếp.

## 3. Cơ sở dữ liệu

| Luật | Chi tiết |
|---|---|
| **Khoá chính** | `Guid` sinh bằng `Guid.NewGuid()` trong entity. **Không dùng `int`** |
| **Đặt tên** | Entity tiếng Anh PascalCase **số ít** · bảng **số nhiều** · cột PascalCase · FK `<Entity>Id`. **Không bật `UseSnakeCaseNamingConvention()`** |
| **Tiền** | `numeric(12,2)`. **Không `float`/`double`** — sai số dấu phẩy động trên tiền là lỗi không sửa được |
| **Thời gian** | `timestamptz` (UTC). **Không `timestamp`** thiếu timezone |
| **Toạ độ GPS** | `double precision`. **Không `real`/`float4`** |
| **Trạng thái** | `varchar` qua `.HasConversion<string>()` |
| **Cột audit** | `CreatedAt` ở **mọi bảng** · `UpdatedAt` nullable ở bảng có sửa · **không thêm `IsDeleted`** |

### 🔴 Xoá dữ liệu — lỗi tốn kém nhất trong dự án

EF Core mặc định **`Cascade`** cho mọi quan hệ bắt buộc. **Phải đặt `Restrict` tường minh** cho mọi FK nghiệp vụ — nếu không, xoá một `Route` sẽ cascade xoá sạch **chuyến → vé → thanh toán**, tức là mất dữ liệu doanh thu.

```csharp
modelBuilder.Entity<Ticket>()
    .HasOne(t => t.Trip)
    .WithMany(tr => tr.Tickets)
    .HasForeignKey(t => t.TripId)
    .OnDelete(DeleteBehavior.Restrict);   // KHÔNG để mặc định
```

Chỉ `Cascade` cho: `RouteStops`←`Routes`, `Seats`←`Buses`, `RefreshTokens`←`Users`.

### Chống bán trùng ghế (Sprint 3)

EF Core không sinh được partial index bằng attribute — phải viết trong `OnModelCreating`:

```csharp
entity.HasIndex(t => new { t.TripId, t.SeatId })
    .IsUnique()
    .HasFilter("\"Status\" IN ('Held', 'Paid')");
```

## 4. Bảy quyết định đã chốt — không mở lại

| Quyết định | Chốt là |
|---|---|
| Vé đặt theo | **Cả chuyến** (vẫn lưu `BoardingStopId`/`AlightingStopId` để hiển thị, không phải khoá nghiệp vụ) |
| Vé tháng | **Không kèm ghế** — chỉ là quyền đi lại trên tuyến trong khoảng thời gian |
| Bảng `Schedule` | **Không tách** — dùng `Trips` + API sinh chuyến hàng loạt |
| Số vai trò | **4**: `Admin`, `Manager`, `Driver`, `Passenger`. ERD gốc ghi 5 (có `PhuXe`) là **sai** |
| Thông báo | **In-app qua SignalR** + bảng `Notifications`. Không email/SMS/Web Push |
| Vị trí xe | Trên **`Trips`** (`CurrentLat/Lng`, `CurrentStopId`), **không** trên `Buses` |

Lý do đầy đủ + danh sách 20 bảng: `docs/03-quy-uoc.md` mục **A8**, **A9**.

## 5. Code & API

- Controller `<Danh từ>Controller` · Service `I<Danh từ>Service` · DTO `<Hành động>Request` / `<Danh từ>Response` · hằng số trong lớp `<Tên>Codes`.
- Route API: danh từ **số nhiều, kebab-case** — `GET /api/routes`, `GET /api/monthly-passes`.
- Lỗi trả về **một định dạng duy nhất cho mọi endpoint**: `{ "message": "...", "errors": { "field": ["..."] } }`.
- **Không tự chỉnh format tay** — `.editorconfig` đã cấu hình, VS Code tự đọc.
- Chỉ viết code gọi API **sau khi** endpoint đó có trong `api-contract.md`. Muốn đổi API → sửa contract trước → báo người còn lại.

## 6. Tài liệu

| File | Dùng khi |
|---|---|
| **`docs/03-quy-uoc.md`** | **Nguồn duy nhất** cho mọi quy ước. Cấu trúc: **Phần 0** (đọc trước) · **Phần 1** (tra cứu nhanh) · **Phần 2** (ai được làm gì) · **A–J** (chi tiết). Mục `0.5` viết riêng cho AI đọc |
| `docs/01-kien-truc.md` | Sơ đồ tầng, chia `AppDbContext` theo nghiệp vụ |
| `docs/02-sprint-roadmap.md` | Story nào thuộc sprint nào |
| `api-contract.md` | Endpoint nào trả về gì |

⚠️ **`Huong-dan-thanh-vien-BusTicketing.docx` có 2 chỗ sai nghiêm trọng** — đừng làm theo:
- Ghi **SQL Server / SSMS** → dự án dùng **PostgreSQL (Supabase)**, `UseNpgsql`.
- Ghi tên repo `21xDHoang/bus-ticketing` → thật ra là **`21xDHoang/smart-bus-ticketing`**.

Chi tiết 5 chỗ sai: mục **J** của `docs/03-quy-uoc.md`.

<!-- quy-uoc-nhom:end -->
