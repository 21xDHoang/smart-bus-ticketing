using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IStopSearchService"/> — hợp đồng đầy đủ ở mục "Tra cứu trạm dừng —
/// /stops/search" của docs/api-contract.md.
///
/// <para>
/// <b>Vì sao lọc trong bộ nhớ chứ không lọc ở CSDL.</b> Hợp đồng đòi khớp KHÔNG PHÂN BIỆT DẤU
/// ("cau giay" phải ra "Trạm Cầu Giấy"). PostgreSQL làm được việc đó bằng extension
/// <c>unaccent</c>, nhưng <c>CREATE EXTENSION</c> phải nằm trong một migration — mà
/// <c>Migrations/*</c> là phần của Vàng Thị Dăm (quy ước A7), người khác không được đụng vào.
/// Bên cạnh đó <c>unaccent</c> là hàm riêng của Npgsql nên bộ test chạy trên provider InMemory
/// sẽ đổ — cùng lý do <see cref="RouteSearchService"/> đã ghi khi chọn <c>ToLower().Contains()</c>
/// thay vì <c>EF.Functions.ILike</c>. Bảng trạm của một thành phố chỉ vài trăm dòng và đây là
/// endpoint gợi ý gọi theo từng nhịp gõ, nên đọc trọn bảng rồi xếp hạng trong bộ nhớ là cái giá
/// chấp nhận được, và giữ được <b>một</b> định nghĩa "bỏ dấu" chạy giống nhau ở cả Postgres lẫn
/// test.
/// </para>
///
/// <para>
/// Đường nâng cấp khi bảng trạm lớn dần: thêm một cột chuẩn hoá sẵn (bỏ dấu, viết thường) và
/// đánh chỉ mục trên đó — <b>việc của Dăm</b> (A7), phải phối hợp trước. Lúc đó phép lọc chuyển
/// xuống CSDL, còn <see cref="Normalize"/> ở đây vẫn dùng để chuẩn hoá từ khoá.
/// </para>
///
/// <para>
/// Bản chuẩn hoá này phải cho ra đúng kết quả của <c>normalizeForSearch</c> phía frontend
/// (frontend/src/components/stopSuggest.ts) — nếu lệch, ô gợi ý sẽ đổi hành vi ngay lúc chuyển
/// từ lọc-tại-chỗ sang gọi endpoint này.
/// </para>
/// </summary>
public class StopSearchService : IStopSearchService
{
    // Hạng khớp — số nhỏ đứng trước. Hằng số thường chứ không phải enum: đây là trọng số so sánh
    // nội bộ, không phải một kiểu nghiệp vụ xuất hiện ở chữ ký nào.
    private const int RankNamePrefix = 0;
    private const int RankNameContains = 1;
    private const int RankAddressContains = 2;

    /// <summary>
    /// So tên trạm theo chuẩn <c>vi</c> — hợp đồng chốt "trong cùng một hạng xếp theo tên tăng dần
    /// (chuẩn <c>vi</c>)". Văn hoá <c>vi</c> xếp dấu đúng thứ tự tiếng Việt ("Đống Đa" không lẫn
    /// vào giữa "Đông Anh"), còn so thô theo mã ký tự thì lẫn.
    ///
    /// Dựng một lần ở cấp lớp: <see cref="CultureInfo.GetCultureInfo"/> tốn công tra cứu, mà mỗi
    /// request phải so nhiều lần. Ở chế độ globalization bất biến (không bật trong csproj) thì
    /// trường này ném lỗi ngay lần dùng đầu — ồn ào, chứ không âm thầm đổi thứ tự.
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("vi"), ignoreCase: true);

    private readonly AppDbContext _db;

    public StopSearchService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<StopResponse>>> SearchAsync(
        StopSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // [Range] ở StopSearchRequest đã chặn limit ngoài 1..50 trước khi vào đây; null = "không
        // gửi" → lấy mặc định của hợp đồng.
        var limit = request.Limit ?? StopSearchRequest.DefaultLimit;
        var keyword = Normalize(request.Keyword);

        var stops = await _db.Stops
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Từ khoá rỗng = chế độ duyệt danh sách: hành khách vừa bấm vào ô phải thấy ngay vài trạm
        // để chọn, chứ không phải nghĩ ra từ khoá trước mới thấy gì.
        if (keyword.Length == 0)
        {
            return ServiceResult<IReadOnlyList<StopResponse>>.Ok(
                stops
                    .OrderBy(s => s.Name, NameComparer)
                    // Chốt thêm theo Id: nhiều trạm trùng tên thì hai lần gọi vẫn ra cùng thứ tự
                    // (hợp đồng: "hai lần gọi cùng tham số cho ra cùng một kết quả").
                    .ThenBy(s => s.Id)
                    .Take(limit)
                    .Select(ToResponse)
                    .ToList());
        }

        var scored = new List<(Stop Stop, int Rank)>(stops.Count);

        foreach (var stop in stops)
        {
            var rank = RankOf(stop, keyword);

            // Không khớp gì là chuyện thường — bộ lọc mềm, không phải lỗi.
            if (rank is not null)
            {
                scored.Add((stop, rank.Value));
            }
        }

        // List.Sort KHÔNG ổn định (khác OrderBy của LINQ), nên hai trạm cùng hạng và cùng tên sẽ
        // đổi chỗ giữa hai lần gọi nếu không tự chốt khoá cuối. ThenBy(Id) của nhánh trên là để
        // cùng một mục đích.
        scored.Sort((a, b) =>
        {
            var byRank = a.Rank.CompareTo(b.Rank);
            if (byRank != 0)
            {
                return byRank;
            }

            var byName = NameComparer.Compare(a.Stop.Name, b.Stop.Name);

            return byName != 0 ? byName : a.Stop.Id.CompareTo(b.Stop.Id);
        });

        return ServiceResult<IReadOnlyList<StopResponse>>.Ok(
            scored.Take(limit).Select(entry => ToResponse(entry.Stop)).ToList());
    }

    /// <summary>
    /// Hạng khớp của một trạm với từ khoá <b>đã chuẩn hoá</b>, hoặc <c>null</c> nếu không khớp.
    /// Thứ tự xét chính là thứ tự ưu tiên: tên-bắt-đầu-bằng trước, rồi tên-chứa, cuối cùng là
    /// trạm chỉ khớp nhờ địa chỉ.
    /// </summary>
    private static int? RankOf(Stop stop, string needle)
    {
        var name = Normalize(stop.Name);

        // So Ordinal là đúng ở đây: hai bên đều đã qua Normalize nên đã viết thường và bỏ dấu,
        // không còn gì để văn hoá phải quyết định — dùng so theo văn hoá chỉ thêm chậm và thêm
        // bẫy (kiểu chữ I của tiếng Thổ Nhĩ Kỳ).
        if (name.StartsWith(needle, StringComparison.Ordinal))
        {
            return RankNamePrefix;
        }

        if (name.Contains(needle, StringComparison.Ordinal))
        {
            return RankNameContains;
        }

        // Khớp địa chỉ là để giúp CHỌN ĐÚNG TRẠM: người chỉ nhớ tên đường ("Hai Bà Trưng") vẫn
        // tìm ra được trạm mình cần. Giá trị gửi tiếp cho GET /routes/search vẫn là TÊN trạm, nên
        // vòng khớp này không đổi luật của endpoint kia.
        return Normalize(stop.Address).Contains(needle, StringComparison.Ordinal)
            ? RankAddressContains
            : null;
    }

    /// <summary>
    /// Chuẩn hoá để so khớp: viết thường, bỏ dấu, gộp khoảng trắng, cắt hai đầu.
    ///
    /// Phải thay tay <c>đ → d</c>: <c>đ</c> (U+0111) là một CHỮ CÁI riêng chứ không phải "d" cộng
    /// dấu, nên NFD không tách được nó — quên bước này thì "dong da" không ra "Trạm Đống Đa",
    /// đúng chữ mà người gõ không dấu hay gặp nhất.
    ///
    /// Đây là bản sao có chủ đích của <c>normalizeForSearch</c> bên frontend
    /// (frontend/src/components/stopSuggest.ts) — hai bên phải cho ra cùng kết quả, nên sửa một
    /// bên thì sửa cả hai.
    /// </summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            // Dấu thanh và dấu mũ sau NFD là ký tự tổ hợp rời — bỏ chúng là "bỏ dấu".
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character == 'đ' ? 'd' : character);
        }

        // Tách theo mọi khoảng trắng rồi ghép lại bằng đúng một dấu cách: "  cau   giay " và
        // "cau giay" phải là cùng một từ khoá.
        return string.Join(
            ' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // Chép lại thay vì dùng chung với StopService.ToResponse (private — và luật nhóm không cho
    // sửa file của người khác để mở nó ra). Ánh xạ 5 trường này là hình dạng API đã chốt ở mục
    // "Trạm dừng" của hợp đồng; lệch đi là đổi hình dạng API.
    private static StopResponse ToResponse(Stop stop) => new()
    {
        Id = stop.Id,
        Name = stop.Name,
        Address = stop.Address,
        Latitude = stop.Latitude,
        Longitude = stop.Longitude,
    };
}
