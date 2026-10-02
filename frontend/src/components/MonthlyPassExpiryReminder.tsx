import { Alert, Space, Typography } from 'antd';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { MonthlyPass } from '../api/monthlyPassApi';
import { findPassType } from '../api/monthlyPassApi';
import { getExpiringSoonPasses } from './monthlyPassReminder';

const { Text } = Typography;

interface MonthlyPassExpiryReminderProps {
  /** Các vé tháng của hành khách — component tự lọc ra vé sắp hết hạn. */
  passes: MonthlyPass[];
  /** Mốc "hôm nay" — chỉ để kiểm chứng; bỏ trống thì dùng ngày hiện tại. */
  now?: Dayjs;
}

/** Tên loại vé để nhắc — ví dụ "Vé tháng 1 tháng"; tra cứu lỗi thì hiện mã loại vé. */
function passTypeLabel(pass: MonthlyPass): string {
  return findPassType(pass.passTypeCode)?.name ?? pass.passTypeCode;
}

/**
 * Reminder "vé tháng sắp hết hạn" — task "Nhắc nhở vé tháng sắp hết hạn trên giao diện"
 * (story 16, Nguyễn Đình Băng).
 *
 * Nhận danh sách vé tháng của hành khách rồi tự lọc ra vé còn trong vòng
 * `EXPIRING_SOON_DAYS` ngày (dùng chung `getMonthlyPassStatus`). Không có vé nào sắp hết
 * hạn thì không hiện gì. Màn hình "quản lý vé tháng của tôi" (Thịnh) nhúng component này ở
 * đầu trang; nút "gia hạn" thuộc màn hình đó, không phải ở đây (API gia hạn chưa có).
 */
export default function MonthlyPassExpiryReminder({ passes, now }: MonthlyPassExpiryReminderProps) {
  const expiring = getExpiringSoonPasses(passes, now);

  if (expiring.length === 0) return null;

  return (
    <Alert
      type="warning"
      showIcon
      message={
        expiring.length === 1
          ? 'Vé tháng của bạn sắp hết hạn'
          : `Bạn có ${expiring.length} vé tháng sắp hết hạn`
      }
      description={
        <Space direction="vertical" size={4} style={{ width: '100%' }}>
          {expiring.map(({ pass, daysLeft }) => {
            const date = dayjs(pass.validTo).format('DD/MM/YYYY');
            const remaining =
              daysLeft > 0 ? `còn ${daysLeft} ngày, hết hạn ngày ${date}` : `hết hạn hôm nay (${date})`;

            return (
              <div key={pass.id}>
                <Text strong>{passTypeLabel(pass)}</Text> — {remaining}.
              </div>
            );
          })}
        </Space>
      }
    />
  );
}
