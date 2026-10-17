import { useEffect, useState } from 'react';
import { Button, Empty, Form, Input, InputNumber, List, Modal, Space, Typography, message } from 'antd';
import { ArrowLeftOutlined, DeleteOutlined, PlusOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { PassengerType } from '../api/fareApi';
import { createFareTable, listFareTables, removeFareTable } from '../api/fareTableApi';
import type { FareTable } from '../api/fareTableApi';
import { formatVnd } from './ui/format';

const { Text } = Typography;

/** Hai màn con của modal: xem danh sách bảng giá, hoặc nhập liệu tạo bảng mới. */
type View = 'list' | 'form';

/** Giá trị form tạo bảng giá: một trường tên + năm trường giá theo đúng mã PassengerType. */
type FareTableFormValues = { name: string } & Record<PassengerType, number>;

interface FareTableModalProps {
  open: boolean;
  /** Nhãn tuyến đang chọn để áp bảng giá — null khi chưa chọn tuyến. */
  routeLabel: string | null;
  /** Đang áp bảng giá vào tuyến — tắt nút "Áp dụng" khi true. */
  applying: boolean;
  onCancel: () => void;
  /** Áp bảng giá vào tuyến đang chọn — logic gọi API nằm ở FareConfigPage. */
  onApply: (table: FareTable) => void;
}

/**
 * Modal "Bảng giá mẫu" của màn Cấu hình giá vé (US 12): tạo sẵn các bảng giá (tên + giá đủ 5
 * đối tượng) rồi áp nguyên bảng vào tuyến đang chọn. Bảng giá lưu ở localStorage (fareTableApi)
 * nên chỉ tồn tại trên trình duyệt này — chưa có entity backend.
 */
export default function FareTableModal({
  open,
  routeLabel,
  applying,
  onCancel,
  onApply,
}: FareTableModalProps) {
  const [form] = Form.useForm<FareTableFormValues>();
  const [view, setView] = useState<View>('list');
  const [tables, setTables] = useState<FareTable[]>([]);

  // Mỗi lần mở modal: nạp lại danh sách bảng giá và về lại màn danh sách. Đây là lần đọc dữ
  // liệu thật từ localStorage khi mở modal (không phải đồng bộ state dẫn xuất) nên tắt cảnh
  // báo react/set-state-in-effect cho đúng ngữ cảnh.
  useEffect(() => {
    if (!open) return;
    // oxlint-disable-next-line react/set-state-in-effect
    setTables(listFareTables());
    setView('list');
    form.resetFields();
  }, [open, form]);

  const reload = () => setTables(listFareTables());

  const handleCreate = (values: FareTableFormValues) => {
    const { name, ...prices } = values;
    createFareTable({ name, prices });
    message.success(`Đã tạo bảng giá “${name.trim()}”.`);
    reload();
    setView('list');
  };

  const handleRemove = (table: FareTable) => {
    removeFareTable(table.id);
    message.success(`Đã xoá bảng giá “${table.name}”.`);
    reload();
  };

  return (
    <Modal
      open={open}
      onCancel={onCancel}
      title={view === 'list' ? 'Bảng giá mẫu' : 'Tạo bảng giá mẫu'}
      footer={null}
      width={560}
    >
      {view === 'list' ? (
        <>
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            {/* Tuyến đang chọn để áp — hiện rõ để người dùng biết nút "Áp dụng" nhắm vào đâu. */}
            <Text type="secondary">
              {routeLabel ? (
                <>
                  Áp vào tuyến <Text strong>{routeLabel}</Text>
                </>
              ) : (
                'Chọn một tuyến ở màn hình chính để áp bảng giá.'
              )}
            </Text>

            <Button
              type="primary"
              icon={<PlusOutlined />}
              block
              onClick={() => {
                form.resetFields();
                setView('form');
              }}
            >
              Tạo bảng giá mới
            </Button>
          </Space>

          {tables.length === 0 ? (
            <Empty
              style={{ marginTop: 24 }}
              description="Chưa có bảng giá mẫu nào. Bấm “Tạo bảng giá mới” để thêm."
            />
          ) : (
            <List
              style={{ marginTop: 8 }}
              dataSource={tables}
              renderItem={(table) => (
                <List.Item
                  actions={[
                    <Button
                      key="apply"
                      type="primary"
                      size="small"
                      icon={<ThunderboltOutlined />}
                      disabled={!routeLabel || applying}
                      loading={applying}
                      onClick={() => onApply(table)}
                    >
                      Áp dụng
                    </Button>,
                    <Button
                      key="remove"
                      type="text"
                      size="small"
                      danger
                      icon={<DeleteOutlined />}
                      onClick={() => handleRemove(table)}
                    />,
                  ]}
                >
                  <List.Item.Meta
                    title={table.name}
                    description={
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        {PASSENGER_TYPE_OPTIONS.map(
                          (option) => formatVnd(table.prices[option.value]),
                        ).join(' · ')}
                      </Text>
                    }
                  />
                </List.Item>
              )}
            />
          )}
        </>
      ) : (
        <Form form={form} layout="vertical" onFinish={handleCreate}>
          <Form.Item
            name="name"
            label="Tên bảng giá"
            rules={[{ required: true, message: 'Nhập tên bảng giá.' }]}
          >
            <Input placeholder="Ví dụ: Bảng giá nội thành" maxLength={120} />
          </Form.Item>

          {PASSENGER_TYPE_OPTIONS.map((option) => (
            <Form.Item
              key={option.value}
              name={option.value}
              label={option.label}
              rules={[{ required: true, message: 'Nhập giá cho đối tượng này.' }]}
            >
              <InputNumber
                min={0}
                style={{ width: '100%' }}
                placeholder="0"
                addonAfter={PASSENGER_TYPE_META[option.value].isDiscount ? 'ưu đãi' : undefined}
              />
            </Form.Item>
          ))}

          <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
            <Button icon={<ArrowLeftOutlined />} onClick={() => setView('list')}>
              Quay lại
            </Button>
            <Button type="primary" htmlType="submit">
              Lưu bảng giá
            </Button>
          </Space>
        </Form>
      )}
    </Modal>
  );
}
