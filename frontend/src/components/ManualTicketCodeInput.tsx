import { useState } from 'react';
import { Button, Input, Space } from 'antd';

// -----------------------------------------------------------------------------
// Ô nhập mã vé thủ công (US 15 "Soát vé QR", Sprint 4 — task "Component nhập mã vé
// thủ công khi camera không quét được").
//
// Đường lùi khi camera không quét được mã: tài xế gõ/dán mã vé — mã in ngay dưới
// mã QR trên vé của hành khách (màn "Mã QR vé" hiện "Mã vé: SBT1:…") — rồi gửi đi
// soát. Component KHÔNG tự gọi API: nó chuẩn hoá chuỗi rồi đẩy ra ngoài qua
// `onSubmit`, để màn hình Soát vé dùng CHUNG một luồng xử lý với lượt quét camera
// (cùng `ticketValidationApi.validate`). Nhờ vậy khi backend thật và hàng đợi
// ngoại tuyến (docs/39 §3) được nối vào thì chỉ còn MỘT chỗ gọi.
//
// Vì sao cắt HẾT khoảng trắng chứ không chỉ cắt hai đầu: mã vé không bao giờ chứa
// khoảng trắng, mà mã dài (~106 ký tự) khi dán từ màn vé hoặc PDF hay dính dấu
// xuống dòng ở giữa — chỉ cắt hai đầu sẽ để lại mã hỏng theo cách rất khó thấy.
// -----------------------------------------------------------------------------

/** Trần độ dài mã vé theo hợp đồng soát vé (docs/29 §2: `code` ≤ 200 ký tự). */
const MAX_CODE_LENGTH = 200;

interface ManualTicketCodeInputProps {
  /** Gọi khi tài xế chốt mã — chuỗi đã cắt hết khoảng trắng, chưa từng rỗng. */
  onSubmit: (code: string) => void;
  /** Khoá thao tác trong lúc hệ thống đang kiểm tra (màn hình đang ở pha "đang soát"). */
  disabled?: boolean;
}

export default function ManualTicketCodeInput({ onSubmit, disabled }: ManualTicketCodeInputProps) {
  const [value, setValue] = useState('');

  const submit = () => {
    const code = value.replace(/\s+/g, '');
    if (!code) return;
    onSubmit(code);
  };

  return (
    <Space.Compact style={{ display: 'flex', width: '100%' }}>
      <Input
        placeholder="Dán hoặc gõ mã vé (VD: SBT1:…)"
        aria-label="Mã vé nhập thủ công"
        value={value}
        onChange={(event) => setValue(event.target.value)}
        onPressEnter={submit}
        maxLength={MAX_CODE_LENGTH}
        allowClear
        disabled={disabled}
      />
      <Button type="primary" onClick={submit} disabled={disabled || !value.trim()}>
        Kiểm tra
      </Button>
    </Space.Compact>
  );
}
