using SmartBus.Api.Data;

namespace SmartBus.Tests;

/// <summary>
/// Chốt hành vi "thiếu cấu hình thì báo rõ cách sửa" của <see cref="ChuoiKetNoiCsdl"/> — lời hứa
/// với thành viên mới (kéo repo về, chạy API lần đầu). Test đỏ nghĩa là hoặc thông báo hướng dẫn
/// bị nuốt, hoặc chỗ dán mẫu lọt qua — cả hai đều trả lại lỗi Npgsql khó hiểu cho người mới.
/// </summary>
public class ChuoiKetNoiCsdlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_chuoi_thi_bao_ro_cach_sua(string? chuoi)
    {
        var loi = Assert.Throws<InvalidOperationException>(() => ChuoiKetNoiCsdl.KiemTra(chuoi));

        // Thông báo phải chỉ đúng hai thứ người mới cần: lệnh chạy nhanh và tài liệu đầy đủ.
        Assert.Contains("scripts/setup-csdl.sh", loi.Message);
        Assert.Contains("docs/25", loi.Message);
    }

    [Fact]
    public void Con_nguyen_cho_dan_mau_trong_file_example_cung_bi_coi_la_chua_cau_hinh()
    {
        // Copy file .example mà quên dán chuỗi thật là kiểu quên phổ biến nhất — phải bắt được
        // ở đây chứ không phải để Npgsql báo lỗi parse khó hiểu.
        var loi = Assert.Throws<InvalidOperationException>(
            () => ChuoiKetNoiCsdl.KiemTra(ChuoiKetNoiCsdl.ChoDan));

        Assert.Contains("scripts/setup-csdl.sh", loi.Message);
    }

    [Fact]
    public void Chuoi_hop_le_tra_ve_nguyen_ven_khong_doi()
    {
        const string chuoiThat =
            "Host=abc.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.x;Password=mat-khau-gia";

        Assert.Equal(chuoiThat, ChuoiKetNoiCsdl.KiemTra(chuoiThat));
    }

    [Fact]
    public void DaCauHinh_phan_biet_duoc_chua_cau_hinh_va_da_cau_hinh()
    {
        Assert.False(ChuoiKetNoiCsdl.DaCauHinh(null));
        Assert.False(ChuoiKetNoiCsdl.DaCauHinh(""));
        Assert.False(ChuoiKetNoiCsdl.DaCauHinh(ChuoiKetNoiCsdl.ChoDan));
        Assert.True(ChuoiKetNoiCsdl.DaCauHinh("Host=x;Database=postgres"));
    }
}
