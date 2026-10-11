import { useState } from 'react';
import { Button, Input, Space, message } from 'antd';
import voucherApi from '../api/voucherApi';
import type { VoucherValidationResponse } from '../api/voucherApi';
import type { AppError } from '../api/axiosClient';
import StatusTag from './ui/StatusTag';

// -----------------------------------------------------------------------------
// Ô nhập mã giảm giá ở trang thanh toán (US 18 "Quản lý Voucher" — task r56 "Component
// nhập mã giảm giá ở trang thanh toán").
//
// Khép kín việc gõ mã → gọi POST /vouchers/validate → báo kết quả ra ngoài:
//   - Chưa có mã hợp lệ: hiện ô nhập + nút "Áp dụng".
//   - Mã hợp lệ: hiện thẻ xanh + nút "Xoá".
//   - Mã không hợp lệ: vẫn giữ ô nhập; kết quả (valid: false) được đẩy ra ngoài để
//     VoucherDiscountSummary (r57) hiện lý do từ chối.
//
// Component chỉ giữ trạng thái GÕ PHÍM (input + đang kiểm tra) cho riêng nó. Kết quả validate
// do cha giữ qua cặp validation/onValidationChange, vì cha cần dùng chung kết quả đó cho
// VoucherDiscountSummary và — ở bước thanh toán — gửi voucherCode lên POST /payments.
// -----------------------------------------------------------------------------

interface VoucherCodeInputProps {
  /** Chuyến khách đang đặt — để backend tra điều kiện tuyến (WrongRoute). */
  tripId: string;
  /** Tổng tiền TRƯỚC giảm giá (VND); null = màn trước không truyền, chưa kiểm tra được. */
  orderAmount: number | null;
  /** Kết quả kiểm tra voucher hiện tại — cha giữ để dùng chung với VoucherDiscountSummary. */
  validation: VoucherValidationResponse | null;
  /** Báo kết quả mới về cha; null khi khách xoá mã. */
  onValidationChange: (validation: VoucherValidationResponse | null) => void;
  /** Tắt thao tác khi đang thanh toán. */
  disabled?: boolean;
}

export default function VoucherCodeInput({
  tripId,
  orderAmount,
  validation,
  onValidationChange,
  disabled,
}: VoucherCodeInputProps) {
  // Mã khách đang gõ — chỉ component này cần, không phơi ra ngoài. Khi mã hợp lệ thì ô nhập
  // được thay bằng thẻ xanh nên chuỗi gõ dở không còn quan trọng; lúc xoá mã mới reset lại.
  const [input, setInput] = useState('');
  const [validating, setValidating] = useState(false);

  const applied = validation != null && validation.valid;

  // Kiểm tra mã để HIỂN THỊ số tiền được giảm. Kết quả chỉ để XEM TRƯỚC — con số chốt nằm ở
  // tầng thanh toán (POST /payments), không phải ở đây.
  const apply = async () => {
    const code = input.trim();
    if (!code) return;

    if (orderAmount === null) {
      message.warning('Chưa có tổng tiền để kiểm tra mã giảm giá.');
      return;
    }

    setValidating(true);
    try {
      const result = await voucherApi.validate({ code, tripId, orderAmount });
      onValidationChange(result);
    } catch (err) {
      const appError = err as AppError;
      message.error(appError.customMessage || 'Không kiểm tra được mã giảm giá.');
    } finally {
      setValidating(false);
    }
  };

  const clear = () => {
    setInput('');
    onValidationChange(null);
  };

  // Mã đã dùng được → thẻ xanh + nút xoá thay cho ô nhập, để khách thấy ngay mã đang áp dụng.
  if (applied) {
    return (
      <Space>
        <StatusTag color="green">{validation.code}</StatusTag>
        <Button type="link" size="small" onClick={clear} disabled={disabled}>
          Xoá
        </Button>
      </Space>
    );
  }

  return (
    <Space.Compact style={{ display: 'flex', width: '100%' }}>
      <Input
        placeholder="Nhập mã (VD: SUMMER10)"
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onPressEnter={apply}
        allowClear
        disabled={disabled}
      />
      <Button loading={validating} onClick={apply} disabled={disabled || !input.trim()}>
        Áp dụng
      </Button>
    </Space.Compact>
  );
}
