import { useState } from 'react';
import { Button, Popconfirm, Space, message } from 'antd';
import { FilePdfOutlined, ShareAltOutlined } from '@ant-design/icons';
import ticketPdfApi from '../api/ticketPdfApi';
import type { TicketPdfFile } from '../api/ticketPdfApi';
import type { Ticket } from '../api/ticketApi';
import type { AppError } from '../api/axiosClient';
import { formatDateTime, formatVnd } from './ui/format';

// -----------------------------------------------------------------------------
// Component tải vé PDF + chia sẻ vé — task Sprint 3 dòng 46 (Hoàng Văn Thịnh),
// thuộc US 4 "Vé điện tử QR".
//
// Hai nút cho một vé đã mua:
//   • "Tải vé PDF" — lấy file PDF từ máy chủ (xem ticketPdfApi.ts — đang chạy dữ liệu
//     giả vì endpoint chưa có trong api-contract.md) rồi đẩy xuống trình duyệt.
//   • "Chia sẻ vé" — bảng chia sẻ của hệ điều hành (điện thoại), máy tính thì sao chép
//     nội dung vé vào clipboard. Hỏi lại trước khi chia sẻ vì mã vé dùng để lên xe —
//     người nhận có thể lên thay bạn, và lượt quét đầu tiên thắng.
//
// Vé đã dùng / hết hạn / đã huỷ vẫn tải và chia sẻ được — file PDF là bản ghi chuyến đi
// chứ không phải cửa soát vé; việc chặn quét lại đã do mã QR ở màn hình vé lo (bôi xám).
//
// Màn hình cha (danh sách vé của Hạnh / màn mã QR của Băng) chỉ cần một dòng:
//
//   <TicketDownloadShare ticket={ticket} size="small" />
//
// Component không tự gọi API lấy vé — nhận sẵn `ticket` từ màn hình cha.
// -----------------------------------------------------------------------------

/** Kích cỡ hai nút — dòng vé trong danh sách dùng 'small', màn chi tiết để mặc định. */
type ButtonSize = 'small' | 'middle';

interface TicketDownloadShareProps {
  ticket: Ticket;
  size?: ButtonSize;
}

/**
 * Nội dung chia sẻ — đủ để người nhận biết là chuyến nào và trình mã vé khi lên xe.
 * Cố ý KHÔNG kèm đường link màn hình mã QR: mở link phải đăng nhập, người nhận không có
 * tài khoản thì chỉ thấy trang đăng nhập — thà đưa thẳng thông tin cần thiết.
 */
export function buildTicketShareText(ticket: Ticket): string {
  return [
    `Vé xe buýt ${ticket.routeCode} — ${ticket.routeName}`,
    `Khởi hành: ${formatDateTime(ticket.departureTime)}`,
    `Lên xe tại: ${ticket.boardingStopName}`,
    `Ghế: ${ticket.seatNumber}`,
    `Giá: ${formatVnd(ticket.price)}`,
    `Mã vé (trình khi lên xe): ${ticket.code}`,
  ].join('\n');
}

/** Đẩy blob xuống trình duyệt bằng thẻ <a download> tạm — không mở tab mới. */
function savePdfFile(file: TicketPdfFile): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export default function TicketDownloadShare({
  ticket,
  size = 'middle',
}: TicketDownloadShareProps) {
  const [downloading, setDownloading] = useState(false);

  async function handleDownload() {
    setDownloading(true);
    try {
      const file = await ticketPdfApi.download(ticket);
      savePdfFile(file);
      message.success('Đã tải vé PDF. Mở tệp để xem hoặc in vé.');
    } catch (err) {
      message.error((err as AppError).customMessage || 'Không tải được vé PDF. Vui lòng thử lại.');
    } finally {
      setDownloading(false);
    }
  }

  async function handleShare() {
    const text = buildTicketShareText(ticket);

    // Điện thoại có bảng chia sẻ của hệ điều hành — dùng luôn (gửi qua Zalo, tin nhắn…).
    if (navigator.share) {
      try {
        await navigator.share({ title: 'Vé xe buýt của tôi', text });
        return;
      } catch (err) {
        // Người dùng bấm huỷ bảng chia sẻ — không phải lỗi, không báo gì.
        if ((err as Error).name === 'AbortError') return;
        // Hỏng vì lý do khác thì rơi xuống đường sao chép bên dưới.
      }
    }

    // Máy tính (hoặc bảng chia sẻ hỏng): sao chép nội dung để dán vào tin nhắn.
    try {
      await navigator.clipboard.writeText(text);
      message.success('Đã sao chép thông tin vé — dán vào tin nhắn để gửi.');
    } catch {
      message.error('Trình duyệt không cho chia sẻ. Hãy chụp màn hình mã QR để gửi.');
    }
  }

  return (
    <Space wrap>
      <Button
        size={size}
        icon={<FilePdfOutlined />}
        loading={downloading}
        onClick={handleDownload}
      >
        Tải vé PDF
      </Button>

      <Popconfirm
        title="Chia sẻ vé này?"
        description="Mã vé dùng để lên xe — người nhận có thể lên thay bạn. Chỉ chia sẻ với người bạn tin tưởng."
        okText="Chia sẻ"
        cancelText="Thôi"
        onConfirm={handleShare}
      >
        <Button size={size} icon={<ShareAltOutlined />}>
          Chia sẻ vé
        </Button>
      </Popconfirm>
    </Space>
  );
}
