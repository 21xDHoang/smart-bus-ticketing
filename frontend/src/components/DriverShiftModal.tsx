import { useEffect, useState } from 'react';
import { Descriptions, Modal, Space, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { DRIVER_TRIP_PAGE_SIZE, fetchDriverTrips } from '../api/driverApi';
import type { Driver, DriverTrip } from '../api/driverApi';
import type { AppError } from '../api/axiosClient';
import { TRIP_STATUS_META } from '../api/tripApi';
import { SHIFT_STATE_META, deriveShiftSummary, describeShift, vietnamDayRange } from './driverShift';

const { Text } = Typography;

interface DriverShiftModalProps {
  open: boolean;
  /** Tài xế đang xem. null = chưa chọn (modal đóng). */
  driver: Driver | null;
  onClose: () => void;
}

const TRIP_COLUMNS: TableProps<DriverTrip>['columns'] = [
  {
    title: 'Mã tuyến',
    dataIndex: 'routeCode',
    key: 'routeCode',
    width: 100,
    render: (routeCode: string) => <Tag color="blue">{routeCode}</Tag>,
  },
  {
    title: 'Tuyến',
    dataIndex: 'routeName',
    key: 'routeName',
  },
  {
    title: 'Xe',
    dataIndex: 'busLicensePlate',
    key: 'busLicensePlate',
    width: 130,
  },
  {
    title: 'Khởi hành',
    dataIndex: 'departureTime',
    key: 'departureTime',
    width: 110,
    render: (departureTime: string) => dayjs(departureTime).format('HH:mm'),
  },
  {
    title: 'Dự kiến tới',
    dataIndex: 'arrivalTime',
    key: 'arrivalTime',
    width: 120,
    render: (arrivalTime: string | null) =>
      arrivalTime ? dayjs(arrivalTime).format('HH:mm') : <Text type="secondary">Chưa chốt</Text>,
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    key: 'status',
    width: 150,
    render: (status: DriverTrip['status']) => {
      // Backend chỉ trả bốn mã trong TRIP_STATUS_META. Lỡ có giá trị lạ thì hiện nguyên văn,
      // không để màn hình vỡ vì tra meta không thấy — cùng lối các màn hình chuyến khác.
      const meta = TRIP_STATUS_META[status];
      return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{status}</Tag>;
    },
  },
];

// Modal ca làm việc của một tài xế: hồ sơ rút gọn + các chuyến được phân công hôm nay.
//
// Tự gọi API lấy chuyến thay vì nhận qua prop: màn hình danh sách chỉ cần biết trạng thái ca
// (một dòng tóm tắt), không cần giữ sẵn toàn bộ chuyến của mọi tài xế trong bộ nhớ.
export default function DriverShiftModal({ open, driver, onClose }: DriverShiftModalProps) {
  const [trips, setTrips] = useState<DriverTrip[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !driver) return;

    let cancelled = false;
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    setLoadError(null);

    const { from, to } = vietnamDayRange(dayjs());

    fetchDriverTrips(driver.id, { from, to, page: 1, pageSize: DRIVER_TRIP_PAGE_SIZE })
      .then((result) => {
        if (!cancelled) setTrips(result.items);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setTrips([]);
        setLoadError((err as AppError).customMessage || 'Không thể tải ca làm việc.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [open, driver]);

  const today = dayjs();
  const summary = deriveShiftSummary(trips, today);
  const shiftMeta = SHIFT_STATE_META[summary.state];

  return (
    <Modal
      title="Ca làm việc hôm nay"
      open={open}
      onCancel={onClose}
      onOk={onClose}
      okText="Đóng"
      cancelButtonProps={{ style: { display: 'none' } }}
      width={860}
    >
      {driver && (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label="Tài xế">{driver.fullName}</Descriptions.Item>
            <Descriptions.Item label="Điện thoại">{driver.phoneNumber}</Descriptions.Item>
            <Descriptions.Item label="Email">
              {driver.email || <Text type="secondary">Chưa có</Text>}
            </Descriptions.Item>
            <Descriptions.Item label="Tài khoản">
              {driver.isActive ? (
                <Tag color="success">Đang làm</Tag>
              ) : (
                <Tag color="default">Đã khoá</Tag>
              )}
            </Descriptions.Item>
          </Descriptions>

          <div>
            <Space align="center" wrap>
              <Text strong>{today.format('DD/MM/YYYY')}</Text>
              <Tag color={shiftMeta.color}>{shiftMeta.label}</Tag>
              {!loading && !loadError && (
                <Text type="secondary">{describeShift(summary)}</Text>
              )}
            </Space>
          </div>

          {loadError ? (
            <Text type="danger">{loadError}</Text>
          ) : (
            <Table<DriverTrip>
              rowKey="id"
              size="small"
              columns={TRIP_COLUMNS}
              dataSource={trips}
              loading={loading}
              pagination={false}
              scroll={{ x: 760 }}
              locale={{
                emptyText: loading
                  ? 'Đang tải ca làm việc…'
                  : 'Hôm nay tài xế chưa được phân công chuyến nào',
              }}
            />
          )}
        </Space>
      )}
    </Modal>
  );
}
