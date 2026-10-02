import { useEffect, useState } from 'react';
import { Button, Popconfirm, Select, Space, Typography } from 'antd';
import type { DriverOption } from '../api/tripAssignmentApi';

const { Text } = Typography;

interface TripBulkAssignBarProps {
  /** Số chuyến chưa phân công đang được chọn. 0 = ẩn thanh công cụ. */
  selectedCount: number;
  /** Danh sách tài xế đang hoạt động cho ô chọn. */
  drivers: DriverOption[];
  /** Đang tải danh sách tài xế. */
  loadingDrivers: boolean;
  /** Trang cha bật loading cho nút gán trong lúc gọi API. */
  submitting: boolean;
  /** Bỏ chọn toàn bộ. */
  onClear: () => void;
  /** Gán tài xế đã chọn cho các chuyến đang chọn. */
  onAssign: (driverId: string) => void;
}

/**
 * Thanh "phân công tài xế hàng loạt" — task 120, Dương Thị Hạnh.
 *
 * Hiện khi có ít nhất một chuyến chưa phân công được chọn trong bảng: chọn một tài xế rồi
 * gán cho toàn bộ chuyến đang chọn trong một thao tác. Khác nút "Phân công" từng dòng
 * (đổi XE) — thanh này chỉ phụ trách gán TÀI XẾ hàng loạt.
 */
export default function TripBulkAssignBar({
  selectedCount,
  drivers,
  loadingDrivers,
  submitting,
  onClear,
  onAssign,
}: TripBulkAssignBarProps) {
  const [driverId, setDriverId] = useState<string>();

  // Hết chọn (sau khi gán thành công hoặc bỏ chọn) thì reset ô chọn tài xế.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    if (selectedCount === 0) setDriverId(undefined);
  }, [selectedCount]);

  if (selectedCount === 0) return null;

  const handleAssign = () => {
    if (!driverId) return;
    onAssign(driverId);
  };

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        flexWrap: 'wrap',
        gap: 12,
        marginBottom: 16,
        padding: '10px 16px',
        background: '#eef1ff',
        borderRadius: 10,
      }}
    >
      <Text>
        Đã chọn <Text strong>{selectedCount}</Text> chuyến chưa phân công.
      </Text>

      <Space wrap>
        <Select
          showSearch
          optionFilterProp="label"
          placeholder="Chọn tài xế"
          style={{ minWidth: 240 }}
          value={driverId}
          loading={loadingDrivers}
          onChange={(value) => setDriverId(value)}
          options={drivers.map((driver) => ({
            value: driver.id,
            label: `${driver.fullName} — ${driver.phoneNumber}`,
          }))}
          notFoundContent="Không có tài xế đang hoạt động nào"
        />

        <Popconfirm
          title="Phân công tài xế hàng loạt?"
          description={`Tài xế sẽ được gán cho ${selectedCount} chuyến đang chọn.`}
          okText="Gán"
          cancelText="Huỷ"
          onConfirm={handleAssign}
          disabled={!driverId}
        >
          <Button type="primary" disabled={!driverId} loading={submitting}>
            Gán tài xế
          </Button>
        </Popconfirm>

        <Button onClick={onClear} disabled={submitting}>
          Bỏ chọn
        </Button>
      </Space>
    </div>
  );
}
