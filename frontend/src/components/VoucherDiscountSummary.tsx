import { Tag, Typography, theme } from 'antd';
import type { VoucherValidationResponse } from '../api/voucherApi';
import { VOUCHER_REASON_META } from '../api/voucherApi';
import { formatVnd } from './ui/format';

const { Text } = Typography;

// -----------------------------------------------------------------------------
// Hiển thị số tiền được giảm và giá sau giảm (US 18 "Quản lý Voucher", Sprint 3 — task dòng 58
// của Nguyễn Đình Băng). Component TRÌNH BÀY thuần: nhận tổng tiền + kết quả POST /vouchers/validate
// rồi vẽ ra ba dòng — tổng tiền tạm tính, số tiền được giảm, giá sau giảm — hoặc câu lý do khi mã
// không dùng được. Không tự gọi API: việc gọi `voucherApi.validate` thuộc về ô nhập mã (component
// "nhập mã giảm giá" của Hoàng Văn Thịnh) hoặc lối tắt tạm trên màn thanh toán.
//
// Kết quả validate chỉ là XEM TRƯỚC; con số chốt lại nằm ở tầng thanh toán, nên component này chỉ
// hiển thị, không lưu trạng thái gì.
// -----------------------------------------------------------------------------

interface VoucherDiscountSummaryProps {
  /** Tổng tiền TRƯỚC giảm giá (VND); null = chưa có tổng (màn trước không truyền). */
  orderAmount: number | null;
  /** Kết quả kiểm tra voucher — null khi khách chưa áp dụng mã nào. */
  validation: VoucherValidationResponse | null;
}

export default function VoucherDiscountSummary({
  orderAmount,
  validation,
}: VoucherDiscountSummaryProps) {
  const { token } = theme.useToken();
  const applied = validation != null && validation.valid;

  // Chưa có voucher hợp lệ → giữ nguyên tổng tiền tạm tính nổi bật như trước, kèm lý do nếu có.
  if (!applied) {
    return (
      <div>
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'baseline',
            gap: 12,
          }}
        >
          <Text type="secondary">Tổng tiền tạm tính</Text>
          <div style={{ fontSize: 22, fontWeight: 700, color: token.colorPrimary }}>
            {orderAmount === null ? '—' : formatVnd(orderAmount)}
          </div>
        </div>

        {validation != null && !validation.valid && (
          <div style={{ marginTop: 8 }}>
            <Text type="danger">
              {VOUCHER_REASON_META[validation.reasonCode ?? 'NotFound']?.label ??
                validation.message ??
                'Mã giảm giá không dùng được.'}
            </Text>
          </div>
        )}
      </div>
    );
  }

  // Voucher hợp lệ → bóc tách thành ba dòng: tổng tiền (gạch ngang) → giảm → giá sau giảm.
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
        <Text type="secondary">Tổng tiền tạm tính</Text>
        <Text type="secondary" delete>
          {formatVnd(orderAmount ?? 0)}
        </Text>
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12 }}>
        <Text type="secondary">
          Số tiền được giảm{' '}
          <Tag color="green" style={{ marginInlineStart: 4 }}>
            {validation.code}
          </Tag>
        </Text>
        <Text type="success" strong>
          -{formatVnd(validation.discountAmount)}
        </Text>
      </div>

      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'baseline',
          borderTop: `1px solid ${token.colorBorderSecondary}`,
          paddingTop: 12,
        }}
      >
        <Text strong>Giá sau giảm</Text>
        <div style={{ fontSize: 22, fontWeight: 700, color: token.colorPrimary }}>
          {formatVnd(validation.finalAmount)}
        </div>
      </div>

      {validation.discountType === 'Percent' && validation.maxDiscount != null && (
        <div style={{ marginTop: 8 }}>
          <Text type="secondary" style={{ fontSize: 12 }}>
            Giảm tối đa {formatVnd(validation.maxDiscount)}
          </Text>
        </div>
      )}
    </div>
  );
}
