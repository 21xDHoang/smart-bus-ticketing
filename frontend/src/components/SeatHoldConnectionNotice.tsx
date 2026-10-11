import { Alert } from 'antd';
import type { SeatHoldSession } from '../api/seatHoldApi';
import { useSeatHoldConnection } from './useSeatHoldConnection';
import type { SeatHoldConnectionPhase } from './useSeatHoldConnection';

interface SeatHoldConnectionNoticeProps {
  /** Phiên giữ chỗ đang theo dõi. null = chưa có phiên (không hiện gì). */
  session: SeatHoldSession | null;
  /**
   * Phiên mới nhất sau mỗi lượt đối chiếu với máy chủ — màn hình cha cập nhật state
   * phiên của mình bằng chính hàm này (thường chỉ là `setSession`). Nhận cả phiên đã
   * Expired/Released để phần tự động nhả ghế (Dương Thị Hạnh) phản ứng theo.
   */
  onResync: (session: SeatHoldSession) => void;
}

interface Notice {
  type: 'info' | 'success' | 'warning' | 'error';
  message: string;
  description: string;
}

/**
 * Lời báo cho từng pha mất kết nối — câu chữ viết cho hành khách đang đứng ở bến với
 * wifi chập chờn: nói rõ phiên còn hay mất, và việc cần làm tiếp theo.
 */
const NOTICES: Record<SeatHoldConnectionPhase, Notice> = {
  Offline: {
    type: 'warning',
    message: 'Mất kết nối mạng',
    description:
      'Phiên giữ chỗ vẫn tiếp tục đếm ngược trên máy chủ. Hãy kết nối lại trước khi hết hạn để giữ được ghế.',
  },
  Reconnecting: {
    type: 'info',
    message: 'Đang kiểm tra lại phiên giữ chỗ…',
    description: 'Vừa có mạng trở lại, đang đối chiếu trạng thái giữ chỗ với máy chủ.',
  },
  ResyncFailed: {
    type: 'warning',
    message: 'Chưa kiểm tra lại được phiên giữ chỗ',
    description: 'Mạng đã trở lại nhưng chưa liên lạc được với máy chủ. Hệ thống sẽ tự thử lại.',
  },
  Reconnected: {
    type: 'success',
    message: 'Đã kết nối lại',
    description: 'Phiên giữ chỗ vẫn còn hiệu lực.',
  },
  Lost: {
    type: 'error',
    message: 'Phiên giữ chỗ đã kết thúc trong lúc mất kết nối',
    description:
      'Thời gian giữ ghế đã hết khi không có mạng nên ghế đã được nhả. Hãy quay lại chọn ghế nếu vẫn muốn đặt.',
  },
};

// -----------------------------------------------------------------------------
// Thông báo xử lý mất kết nối trong lúc giữ chỗ — task Sprint 3 dòng 23
// (Hoàng Văn Thịnh), phục vụ US 3 "Tự động giữ chỗ trong 10 phút".
//
// Vỏ hiển thị của useSeatHoldConnection: nhận phiên + onResync, tự theo dõi mạng và
// đối chiếu với máy chủ, rồi dịch pha thành một Alert. Bình thường (hoặc chưa có
// phiên) không hiện gì. Màn hình cha (màn chọn ghế — luồng giữ chỗ của Băng đang chờ
// nối API) chỉ cần một dòng:
//
//   <SeatHoldConnectionNotice session={session} onResync={setSession} />
//
// Banner đếm ngược (SeatHoldCountdown) đứng ngay cạnh vẫn tick đều trong lúc mất
// mạng — nó đếm bằng giờ máy khách, còn banner này lo phần chỉ máy chủ mới biết:
// phiên còn thật hay đã bị nhả.
// -----------------------------------------------------------------------------
export default function SeatHoldConnectionNotice({
  session,
  onResync,
}: SeatHoldConnectionNoticeProps) {
  const phase = useSeatHoldConnection(session, onResync);

  if (phase === null) return null;

  const notice = NOTICES[phase];

  return (
    <Alert
      type={notice.type}
      showIcon
      message={notice.message}
      description={notice.description}
    />
  );
}
