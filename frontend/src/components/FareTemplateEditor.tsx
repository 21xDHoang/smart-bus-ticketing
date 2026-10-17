import { useState } from 'react';
import { Button, InputNumber, Select, Space, Table, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { SaveOutlined } from '@ant-design/icons';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { DiscountedPassengerType, PassengerType } from '../api/fareApi';
import { loadFareTemplate, saveFareTemplate } from '../api/fareTemplateApi';
import type { PerKmFareTemplate } from '../api/fareTemplateApi';
import { applyDiscount } from '../utils/fareTemplateCalc';
import type { DiscountKind, FareDiscount } from '../utils/fareTemplateCalc';
import { formatVnd } from './ui/format';
import { StatusTag } from './ui';

const { Text } = Typography;

/** Cách ưu đãi cho ô chọn. */
const DISCOUNT_KIND_OPTIONS: { value: DiscountKind; label: string }[] = [
  { value: 'percent', label: 'Giảm %' },
  { value: 'amount', label: 'Giảm tiền' },
];

/**
 * Bảng giá mẫu theo km (US 12): người lớn nhập giá gốc của 1 km; bốn đối tượng ưu đãi nhập cách
 * giảm (theo % hoặc số tiền) áp lên giá người lớn. Bảng hiển thị luôn giá/km sau ưu đãi để admin
 * thấy ngay kết quả. Lưu xuống localStorage qua fareTemplateApi.
 */
export default function FareTemplateEditor() {
  const [template, setTemplate] = useState<PerKmFareTemplate>(() => loadFareTemplate());

  const setAdultPrice = (value: number | null) => {
    setTemplate((prev) => ({ ...prev, adultPricePerKm: value ?? 0 }));
  };

  const setDiscount = (type: DiscountedPassengerType, patch: Partial<FareDiscount>) => {
    setTemplate((prev) => ({
      ...prev,
      discounts: {
        ...prev.discounts,
        [type]: { ...prev.discounts[type], ...patch },
      },
    }));
  };

  const handleSave = () => {
    saveFareTemplate(template);
    message.success('Đã lưu bảng giá mẫu.');
  };

  const columns: TableProps<PassengerType>['columns'] = [
    {
      title: 'Đối tượng',
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
      title: 'Giá gốc / ưu đãi trên mỗi km',
      key: 'config',
      render: (_, type) => {
        if (type === 'Standard') {
          return (
            <InputNumber
              min={0}
              value={template.adultPricePerKm}
              onChange={setAdultPrice}
              addonAfter="₫/km"
              style={{ width: 200 }}
            />
          );
        }

        const discount = template.discounts[type as DiscountedPassengerType];
        return (
          <Space>
            <Select
              value={discount.kind}
              options={DISCOUNT_KIND_OPTIONS}
              style={{ width: 110 }}
              onChange={(kind: DiscountKind) => setDiscount(type as DiscountedPassengerType, { kind })}
            />
            <InputNumber
              min={0}
              max={discount.kind === 'percent' ? 100 : undefined}
              value={discount.value}
              onChange={(value) => setDiscount(type as DiscountedPassengerType, { value: value ?? 0 })}
              addonAfter={discount.kind === 'percent' ? '%' : '₫'}
              style={{ width: 160 }}
            />
          </Space>
        );
      },
    },
    {
      title: 'Giá/km sau ưu đãi',
      key: 'result',
      align: 'right',
      render: (_, type) => {
        const pricePerKm =
          type === 'Standard'
            ? template.adultPricePerKm
            : applyDiscount(template.adultPricePerKm, template.discounts[type as DiscountedPassengerType]);
        return <Text strong>{formatVnd(pricePerKm)}/km</Text>;
      },
    },
  ];

  return (
    <div>
      <Table<PassengerType>
        rowKey={(type) => type}
        columns={columns}
        dataSource={PASSENGER_TYPE_OPTIONS.map((option) => option.value)}
        pagination={false}
        size="middle"
      />

      <div style={{ marginTop: 16, textAlign: 'right' }}>
        <Button type="primary" icon={<SaveOutlined />} onClick={handleSave}>
          Lưu bảng giá mẫu
        </Button>
      </div>
    </div>
  );
}
