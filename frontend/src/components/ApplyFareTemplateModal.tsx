import { Alert, Modal, Space, Table, Typography } from 'antd';
import type { TableProps } from 'antd';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { PassengerType } from '../api/fareApi';
import { loadFareTemplate } from '../api/fareTemplateApi';
import { applyDiscount, routeFare } from '../utils/fareTemplateCalc';
import { formatVnd } from './ui/format';
import { StatusTag } from './ui';

const { Text } = Typography;

/** Một dòng xem trước giá áp dụng. */
interface PreviewRow {
  type: PassengerType;
  price: number;
}

interface ApplyFareTemplateModalProps {
  open: boolean;
  /** Nhãn tuyến đang chọn để áp. */
  routeLabel: string;
  /** Số km của tuyến — giá từng đối tượng = giá/km sau ưu đãi × số km. */
  distanceKm: number;
  /** Số dòng giá hiện có của tuyến — > 0 thì hiện cảnh báo ghi đè. */
  existingCount: number;
  applying: boolean;
  onCancel: () => void;
  /** Lưu các giá đã tính vào tuyến — logic gọi API nằm ở FareConfigPage. */
  onApply: () => void;
}

/**
 * Modal xem trước giá khi áp bảng giá mẫu theo km vào một tuyến. Giá từng đối tượng được tính
 * từ bảng mẫu (giá/km sau ưu đãi) nhân số km tuyến rồi làm tròn về đồng — admin xem lại rồi mới
 * bấm "Lưu". Tuyến đã có giá thì hiện cảnh báo ghi đè để admin xác nhận.
 */
export default function ApplyFareTemplateModal({
  open,
  routeLabel,
  distanceKm,
  existingCount,
  applying,
  onCancel,
  onApply,
}: ApplyFareTemplateModalProps) {
  const template = loadFareTemplate();
  const rows: PreviewRow[] = PASSENGER_TYPE_OPTIONS.map((option) => {
    const pricePerKm =
      option.value === 'Standard'
        ? template.adultPricePerKm
        : applyDiscount(template.adultPricePerKm, template.discounts[option.value]);
    return { type: option.value, price: routeFare(pricePerKm, distanceKm) };
  });

  const columns: TableProps<PreviewRow>['columns'] = [
    {
      title: 'Đối tượng',
      dataIndex: 'type',
      key: 'type',
      render: (type: PassengerType) => {
        const meta = PASSENGER_TYPE_META[type];
        return (
          <Space>
            <StatusTag color={meta.color}>{meta.label}</StatusTag>
            {meta.isDiscount && <StatusTag color="gold">Ưu đãi</StatusTag>}
          </Space>
        );
      },
    },
    {
      title: 'Giá áp dụng',
      dataIndex: 'price',
      key: 'price',
      align: 'right',
      render: (price: number) => <Text strong>{formatVnd(price)}</Text>,
    },
  ];

  return (
    <Modal
      open={open}
      onCancel={onCancel}
      title="Áp dụng bảng giá mẫu"
      okText="Lưu"
      cancelText="Huỷ"
      okButtonProps={{ danger: existingCount > 0, loading: applying }}
      onOk={onApply}
      width={480}
    >
      <Text type="secondary">
        Áp cho tuyến <Text strong>{routeLabel}</Text> · {distanceKm} km
      </Text>

      {existingCount > 0 && (
        <Alert
          style={{ marginTop: 12 }}
          type="warning"
          showIcon
          message={`Tuyến này đã có ${existingCount} dòng giá. Lưu sẽ ghi đè các giá đó.`}
        />
      )}

      <Table<PreviewRow>
        style={{ marginTop: 12 }}
        rowKey="type"
        columns={columns}
        dataSource={rows}
        pagination={false}
        size="small"
      />
    </Modal>
  );
}
