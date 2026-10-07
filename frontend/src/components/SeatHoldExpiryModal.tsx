import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Alert, Button, Modal, Space, Typography } from 'antd';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import seatHoldApi from '../api/seatHoldApi';
import { SEAT_HOLD_DURATION_SECONDS } from '../api/seatHoldApi';
import type { SeatHoldSession } from '../api/seatHoldApi';
import { formatHoldCountdown, getSeatHoldTimeInfo } from './seatHoldReminder';

const { Text } = Typography;

interface SeatHoldExpiryModalProps {
  /** Modal có đang mở không. */
  open: boolean;
  /** Phiên giữ chỗ đang hiển thị. null = chưa có phiên (modal đóng). */
  session: SeatHoldSession | null;
  /** Đóng modal (người dùng bỏ qua cảnh báo, giữ nguyên trạng thái phiên). */
  onClose: () => void;
  /** Quay về màn chọn ghế — dùng khi hết hạn hoặc người dùng muốn bỏ phiên này. */
  onBackToSeats: () => void;
  /** Gia hạn thành công — báo phiên mới để màn hình cha đồng bộ (expiresAt mới, canExtend=false). */
  onExtended?: (session: SeatHoldSession) => void;
  /** Mốc "bây giờ" — chỉ để kiểm chứng; bỏ trống thì component tự đếm giờ. */
  now?: Dayjs;
}

/** Trạng thái của thao tác gia hạn — ba nhánh tách bạch như mọi màn hình khác. */
type ExtendState =
  | { status: 'idle' }
  | { status: 'extending' }
  | { status: 'success'; session: SeatHoldSession }
  | { status: 'error'; message: string };

// Modal "thông báo hết hạn giữ chỗ + nút gia hạn" — task Sprint 3 "Modal thông báo hết hạn
// giữ chỗ + nút gia hạn" (US 3, Nguyễn Đình Băng).
//
// Hiện ra khi phiên giữ chỗ sắp hết hạn (hoặc đã hết hạn) để cảnh báo hành khách: giữ ghế chỉ
// kéo dài 10 phút, hết hạn là ghế bị nhả. Người dùng chọn "Gia hạn" (thêm 10 phút, tối đa 1
// lần) hoặc "Quay về chọn ghế". Việc TỰ ĐỘNG nhả ghế và quay về khi hết hạn là task của Dương
// Thị Hạnh; component đếm ngược liên tục là task của Hoàng Văn Thịnh — modal này chỉ cần đủ để
// thông báo và mở lối gia hạn.
//
// Nhận phiên qua prop và tự gọi API gia hạn bên trong, nên dùng được độc lập mà chưa cần màn
// hình cha nối xong luồng giữ chỗ. Màn hình cha chỉ cần truyền `session` + `open` + hai
// callback; khi nối API thật thì chỉ đổi cờ USE_MOCK_DATA trong seatHoldApi.ts.
export default function SeatHoldExpiryModal({
  open,
  session,
  onClose,
  onBackToSeats,
  onExtended,
  now,
}: SeatHoldExpiryModalProps) {
  // Đồng hồ nội bộ: tick mỗi giây để cập nhật số giây còn lại khi modal đang mở. Có `now` cố
  // định (kiểm chứng) thì không tick — mốc thời gian do bên ngoài bơm vào.
  const [nowState, setNowState] = useState(() => dayjs());
  const [extend, setExtend] = useState<ExtendState>({ status: 'idle' });
  const [prevSessionCode, setPrevSessionCode] = useState(session?.sessionCode);

  useEffect(() => {
    if (!open || now) return;
    const timer = setInterval(() => setNowState(dayjs()), 1000);
    return () => clearInterval(timer);
  }, [open, now]);

  // Mỗi khi đổi phiên thì bỏ trạng thái gia hạn cũ. Điều chỉnh state NGAY LÚC RENDER theo
  // khuyến nghị của React ("adjusting state during render") thay vì effect reset, để không
  // phải tắt cảnh báo react/set-state-in-effect.
  if (session?.sessionCode !== prevSessionCode) {
    setPrevSessionCode(session?.sessionCode);
    setExtend({ status: 'idle' });
  }

  if (!session) return null;

  const clock = now ?? nowState;
  const displayedSession = extend.status === 'success' ? extend.session : session;
  const info = getSeatHoldTimeInfo(displayedSession, clock);
  const seatLabels = session.seatNumbers.join(', ');
  const extendMinutes = SEAT_HOLD_DURATION_SECONDS / 60;

  const handleExtend = async () => {
    setExtend({ status: 'extending' });

    try {
      const renewed = await seatHoldApi.extend(session.sessionCode);
      setExtend({ status: 'success', session: renewed });
      onExtended?.(renewed);
    } catch (error) {
      setExtend({
        status: 'error',
        message: (error as Error).message || 'Không thể gia hạn thời gian giữ chỗ.',
      });
    }
  };

  let title: string;
  let body: ReactNode;
  let footer: ReactNode;

  if (extend.status === 'success') {
    title = 'Đã gia hạn thời gian giữ chỗ';
    body = (
      <Alert
        type="success"
        showIcon
        message={`Ghế ${seatLabels} được giữ thêm ${extendMinutes} phút`}
        description={
          <Text type="secondary">
            Hết hạn mới: {dayjs(extend.session.expiresAt).format('HH:mm:ss')}
          </Text>
        }
      />
    );
    footer = (
      <Button type="primary" onClick={onClose}>
        Đóng
      </Button>
    );
  } else if (info.phase === 'Expired') {
    title = 'Thời gian giữ chỗ đã hết hạn';
    body = (
      <Alert
        type="error"
        showIcon
        message="Ghế đã được nhả"
        description={
          <Text>Ghế {seatLabels} đã hết thời gian giữ. Hãy quay lại chọn ghế để đặt lại.</Text>
        }
      />
    );
    footer = (
      <Button type="primary" onClick={onBackToSeats}>
        Quay về chọn ghế
      </Button>
    );
  } else {
    title = 'Thời gian giữ chỗ sắp hết hạn';
    body = (
      <Alert
        type="warning"
        showIcon
        message={`Ghế ${seatLabels} sẽ được nhả sau ${formatHoldCountdown(info.secondsLeft)}`}
        description={
          <Space direction="vertical" size={4} style={{ width: '100%' }}>
            <Text type="secondary">
              Gia hạn để giữ thêm {extendMinutes} phút (tối đa 1 lần).
            </Text>
            {extend.status === 'error' && <Text type="danger">{extend.message}</Text>}
          </Space>
        }
      />
    );
    footer = (
      <>
        <Button onClick={onBackToSeats}>Quay về chọn ghế</Button>
        <Button
          type="primary"
          loading={extend.status === 'extending'}
          disabled={!info.canExtend}
          onClick={handleExtend}
        >
          Gia hạn thêm {extendMinutes} phút
        </Button>
      </>
    );
  }

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onClose}
      maskClosable={false}
      footer={footer}
      width={480}
    >
      {body}
    </Modal>
  );
}
