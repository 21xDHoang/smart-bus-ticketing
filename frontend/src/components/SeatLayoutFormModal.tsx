import { useEffect, useState } from 'react';
import { Form, Input, InputNumber, Modal, Select, Typography, theme } from 'antd';
import type { AppError } from '../api/axiosClient';
import { SEAT_LAYOUT_LIMITS, buildSeats, layoutGridConfig, seatKeysOf } from '../api/seatLayoutApi';
import type { SeatLayout, SeatLayoutPayload } from '../api/seatLayoutApi';
import SeatMap from './SeatMap';

// Modal thêm/sửa sơ đồ ghế (màn "Admin cấu hình sơ đồ ghế theo loại xe"). Ngoài loại xe và số
// tầng, Admin đặt số hàng + số ghế mỗi hàng rồi bấm chọn ghế VIP trực tiếp trên sơ đồ. Ghế VIP
// là khái niệm duy nhất phân biệt giữa các ghế (backlog ghi "số tầng, số ghế, ghế VIP") — không
// có loại ghế nào khác trong phạm vi story.

interface SeatLayoutFormValues {
  busType: string;
  numberOfFloors: number | null;
  rowsPerFloor: number | null;
  columnsPerRow: number | null;
}

interface SeatLayoutFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa sơ đồ đó. */
  editing: SeatLayout | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /**
   * Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API.
   * Trang cha reject với AppError khi thất bại — modal bắt lại để gắn lỗi từng ô.
   */
  onSubmit: (payload: SeatLayoutPayload, id?: string) => Promise<void>;
}

/** Giá trị mặc định khi thêm mới — 1 tầng, 9 hàng, 5 ghế/hàng = xe 45 chỗ phổ biến. */
const DEFAULT_VALUES = { numberOfFloors: 1, rowsPerFloor: 9, columnsPerRow: 5 };

export default function SeatLayoutFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: SeatLayoutFormModalProps) {
  const [form] = Form.useForm<SeatLayoutFormValues>();
  const { token } = theme.useToken();
  const isEdit = editing !== null;

  // Ghế VIP do Admin bấm chọn trực tiếp trên sơ đồ, giữ ngoài Form (Form chỉ chứa các ô nhập).
  const [vipKeys, setVipKeys] = useState<string[]>([]);

  // Xem trước số tầng/hàng/cột theo giá trị form đang nhập — dùng để vẽ sơ đồ trực tiếp.
  const numberOfFloors = Form.useWatch('numberOfFloors', form) ?? DEFAULT_VALUES.numberOfFloors;
  const rowsPerFloor = Form.useWatch('rowsPerFloor', form) ?? DEFAULT_VALUES.rowsPerFloor;
  const columnsPerRow = Form.useWatch('columnsPerRow', form) ?? DEFAULT_VALUES.columnsPerRow;

  // Mỗi lần mở modal: nạp sơ đồ đang sửa, hoặc reset về mặc định nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      const config = layoutGridConfig(editing);
      form.setFieldsValue({
        busType: editing.busType,
        numberOfFloors: editing.numberOfFloors,
        rowsPerFloor: config.rowsPerFloor,
        columnsPerRow: config.columnsPerRow,
      });
    } else {
      form.resetFields();
    }
    // Ghế VIP đi theo sơ đồ đang mở — đồng bộ từ `editing` khi modal mở ra (cùng lối modal
    // nạp dữ liệu ban đầu, không phải "đồng bộ state dẫn xuất" nên tắt cảnh báo là hợp lý).
    // oxlint-disable-next-line react/set-state-in-effect
    setVipKeys(editing ? layoutGridConfig(editing).vipKeys : []);
  }, [open, editing, form]);

  // Lọc các ghế VIP đã chọn nhưng không còn tồn tại sau khi Admin thu nhỏ sơ đồ.
  const validKeys = seatKeysOf(numberOfFloors, rowsPerFloor, columnsPerRow);
  const activeVipKeys = vipKeys.filter((key) => validKeys.includes(key));
  const totalSeatCount = numberOfFloors * rowsPerFloor * columnsPerRow;

  const toggleVip = (key: string) => {
    setVipKeys((current) =>
      current.includes(key) ? current.filter((item) => item !== key) : [...current, key],
    );
  };

  const handleOk = async () => {
    let values: SeatLayoutFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
      return;
    }

    const floors = values.numberOfFloors ?? DEFAULT_VALUES.numberOfFloors;
    const rows = values.rowsPerFloor ?? DEFAULT_VALUES.rowsPerFloor;
    const cols = values.columnsPerRow ?? DEFAULT_VALUES.columnsPerRow;

    const payload: SeatLayoutPayload = {
      busType: values.busType,
      numberOfFloors: floors,
      seats: buildSeats(floors, rows, cols, activeVipKeys),
    };

    try {
      await onSubmit(payload, editing?.id);
    } catch (error) {
      const appError = error as AppError;
      // Lỗi theo từng trường → gắn thẳng vào ô input; lỗi chung thì trang cha đã toast rồi.
      if (appError?.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof SeatLayoutFormValues,
            errors: fieldErrors,
          })),
        );
      }
    }
  };

  return (
    <Modal
      title={isEdit ? 'Sửa sơ đồ ghế' : 'Thêm sơ đồ ghế'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm sơ đồ'}
      cancelText="Huỷ"
      width={640}
      maskClosable={false}
    >
      <Form form={form} layout="vertical" initialValues={DEFAULT_VALUES}>
        <Form.Item
          name="busType"
          label="Loại xe"
          rules={[
            { required: true, message: 'Vui lòng nhập loại xe' },
            { min: 2, max: 50, message: 'Loại xe từ 2 đến 50 ký tự' },
          ]}
        >
          <Input placeholder="VD: Xe buýt 45 chỗ, Xe buýt điện" maxLength={50} />
        </Form.Item>

        <div style={{ display: 'flex', gap: 16 }}>
          <Form.Item
            name="numberOfFloors"
            label="Số tầng"
            style={{ flex: 1 }}
            rules={[{ required: true, message: 'Vui lòng chọn số tầng' }]}
          >
            <Select options={[1, 2].map((value) => ({ value, label: `${value} tầng` }))} />
          </Form.Item>

          <Form.Item
            name="rowsPerFloor"
            label="Số hàng mỗi tầng"
            style={{ flex: 1 }}
            rules={[
              { required: true, message: 'Vui lòng nhập số hàng' },
              {
                validator: (_, value: number | null | undefined) => {
                  if (value == null) return Promise.resolve();
                  if (value < 1 || value > SEAT_LAYOUT_LIMITS.maxRowsPerFloor) {
                    return Promise.reject(
                      new Error(`Số hàng từ 1 đến ${SEAT_LAYOUT_LIMITS.maxRowsPerFloor}`),
                    );
                  }
                  return Promise.resolve();
                },
              },
            ]}
          >
            <InputNumber
              style={{ width: '100%' }}
              min={1}
              max={SEAT_LAYOUT_LIMITS.maxRowsPerFloor}
              placeholder="VD: 9"
            />
          </Form.Item>

          <Form.Item
            name="columnsPerRow"
            label="Số ghế mỗi hàng"
            style={{ flex: 1 }}
            rules={[
              { required: true, message: 'Vui lòng nhập số ghế' },
              {
                validator: (_, value: number | null | undefined) => {
                  if (value == null) return Promise.resolve();
                  if (value < 2 || value > SEAT_LAYOUT_LIMITS.maxColumnsPerRow) {
                    return Promise.reject(
                      new Error(`Số ghế từ 2 đến ${SEAT_LAYOUT_LIMITS.maxColumnsPerRow}`),
                    );
                  }
                  return Promise.resolve();
                },
              },
            ]}
          >
            <InputNumber
              style={{ width: '100%' }}
              min={2}
              max={SEAT_LAYOUT_LIMITS.maxColumnsPerRow}
              placeholder="VD: 5"
            />
          </Form.Item>
        </div>

        <Form.Item label="Sơ đồ ghế (bấm để chọn ghế VIP)">
          <div
            style={{ border: `1px dashed ${token.colorBorder}`, borderRadius: 12, padding: 16 }}
          >
            <SeatMap
              floors={numberOfFloors}
              rowsPerFloor={rowsPerFloor}
              columnsPerRow={columnsPerRow}
              vipKeys={activeVipKeys}
              interactive
              onToggleVip={toggleVip}
            />
          </div>
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            Tổng {totalSeatCount} ghế · {activeVipKeys.length} ghế VIP
          </Typography.Text>
        </Form.Item>
      </Form>
    </Modal>
  );
}
