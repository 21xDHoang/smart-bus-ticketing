import { useEffect } from 'react';
import { Form, InputNumber, Modal, Select } from 'antd';
import { PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { Fare, FarePayload, PassengerType } from '../api/fareApi';

interface FareFormValues {
  passengerType: PassengerType;
  price: number;
}

interface FareFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa giá của dòng đó. */
  editing: Fare | null;
  /** Các đối tượng tuyến đã có giá — loại khỏi ô chọn khi thêm mới (tránh 409). */
  existingTypes: PassengerType[];
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /** Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API. */
  onSubmit: (payload: FarePayload, id?: string) => void;
}

/** Trần giá khớp FareService.MaxPrice — cột numeric(12,2) của CSDL. */
const MAX_PRICE = 9999999999.99;

export default function FareFormModal({
  open,
  editing,
  existingTypes,
  submitting,
  onCancel,
  onSubmit,
}: FareFormModalProps) {
  const [form] = Form.useForm<FareFormValues>();
  const isEdit = editing !== null;

  // Mỗi lần mở modal: nạp giá đang sửa, hoặc reset về trống nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({ passengerType: editing.passengerType, price: editing.price });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  // Khi thêm mới chỉ chọn được đối tượng chưa có giá. Khi sửa thì cố định đối tượng cũ
  // (PUT chỉ cho đổi giá — muốn đổi đối tượng phải xoá rồi tạo lại, đúng api-contract.md).
  const availableOptions = PASSENGER_TYPE_OPTIONS.filter(
    (option) => !existingTypes.includes(option.value),
  );

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit({ passengerType: values.passengerType, price: values.price }, editing?.id);
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  return (
    <Modal
      title={isEdit ? 'Sửa giá vé' : 'Thêm giá vé'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm giá'}
      cancelText="Huỷ"
      width={480}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="passengerType"
          label="Đối tượng áp dụng"
          rules={[{ required: true, message: 'Vui lòng chọn đối tượng' }]}
        >
          <Select
            placeholder="Chọn đối tượng"
            disabled={isEdit}
            options={isEdit ? PASSENGER_TYPE_OPTIONS : availableOptions}
            notFoundContent="Tuyến này đã có giá cho mọi đối tượng"
          />
        </Form.Item>

        <Form.Item
          name="price"
          label="Giá vé (VND)"
          rules={[
            { required: true, message: 'Vui lòng nhập giá vé' },
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (value <= 0) return Promise.reject(new Error('Giá vé phải lớn hơn 0'));
                if (value > MAX_PRICE) {
                  return Promise.reject(new Error('Giá vé tối đa 9.999.999.999,99'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={0} placeholder="VD: 8000" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
