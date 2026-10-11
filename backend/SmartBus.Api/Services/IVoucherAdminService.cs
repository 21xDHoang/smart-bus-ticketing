using SmartBus.Api.Dtos.Vouchers;

namespace SmartBus.Api.Services;

/// <summary>
/// CRUD voucher cho màn quản trị — GET/POST /api/vouchers, GET/PUT /api/vouchers/{id} và
/// PATCH /api/vouchers/{id}/status (US 18, task *"API CRUD voucher: mã, loại giảm giá, điều kiện áp
/// dụng, số lượng"* — Trần Trung Hiếu, dòng 51). Hợp đồng đầy đủ ở mục "Voucher — /vouchers", phần
/// "CRUD voucher" của docs/api-contract.md.
///
/// Tên "Admin" theo lối <see cref="IAdminUserService"/> và <see cref="IFeedbackAdminService"/>:
/// bề mặt quản trị sau policy ManagerOrAbove. KHÁC hẳn ba service voucher đã có — Validation (xem
/// trước, khách hàng), Redemption (tiêu thụ lúc tiền về), Statistics (đọc gộp) — bốn việc trên một
/// bảng, đừng gộp.
/// </summary>
public interface IVoucherAdminService
{
    /// <summary>
    /// Danh sách voucher + tìm theo mã/tên + lọc trạng thái + phân trang. <c>total</c> là tổng số
    /// dòng khớp bộ lọc; sắp <c>createdAt</c> giảm dần.
    /// </summary>
    Task<ServiceResult<VoucherListResponse>> ListAsync(
        ListVouchersRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Một voucher theo id — không tồn tại → NotFound (404).</summary>
    Task<ServiceResult<VoucherResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo voucher mới. Mã chuẩn hoá CHỮ HOA; trùng mã → Conflict (409); tuyến không tồn tại →
    /// NotFound (404); luật phụ thuộc kiểu giảm giá và thứ tự validFrom/validUntil → Invalid (400).
    /// </summary>
    Task<ServiceResult<VoucherResponse>> CreateAsync(
        CreateVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sửa voucher. Cùng bảng luật của <see cref="CreateAsync"/> cộng <c>quantity ≥ usedCount</c>;
    /// trùng mã bỏ qua chính voucher đang sửa. <c>usedCount</c> không nằm trong request nên không
    /// bao giờ bị đổi.
    /// </summary>
    Task<ServiceResult<VoucherResponse>> UpdateAsync(
        Guid id,
        UpdateVoucherRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Bật/tắt áp dụng — chỉ đổi cột Status và làm mới UpdatedAt.</summary>
    Task<ServiceResult<VoucherResponse>> UpdateStatusAsync(
        Guid id,
        UpdateVoucherStatusRequest request,
        CancellationToken cancellationToken = default);
}
