import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  Card,
  Descriptions,
  Empty,
  Form,
  Select,
  Space,
  Typography,
  message,
} from 'antd';
import { CheckCircleFilled, IdcardOutlined, EnvironmentOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import monthlyPassApi, { PASS_TYPE_OPTIONS, findPassType } from '../api/monthlyPassApi';
import type { MonthlyPass, MonthlyPassRoute, PassTypeCode } from '../api/monthlyPassApi';
import type { AppError } from '../api/axiosClient';
import MonthlyPassStatusTag from '../components/MonthlyPassStatusTag';

const { Title, Text } = Typography;

/** Giá trị của form đăng ký — hai ô chọn tuyến và loại vé. */
interface RegistrationFormValues {
  routeId: string;
  passTypeCode: PassTypeCode;
}

/** Định dạng tiền VND — ví dụ 200000 → "200.000 đ". */
function formatVnd(price: number): string {
  return `${price.toLocaleString('vi-VN')} đ`;
}

// Màn hình đăng ký vé tháng (story 16): chọn tuyến → chọn loại vé → xem giá + thời hạn
// → bấm "Đăng ký" để tạo vé tháng. Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai
// trò như các màn hình quản trị. Backend vé tháng chưa có nên đang chạy trên dữ liệu giả
// (xem monthlyPassApi.ts); khi API xong chỉ cần đổi cờ USE_MOCK_DATA trong file đó.
export default function MonthlyPassRegistrationPage() {
  const [form] = Form.useForm<RegistrationFormValues>();

  // Theo dõi hai ô chọn để vẽ lại ô "xem giá" ngay khi hành khách đổi lựa chọn.
  const routeId = Form.useWatch('routeId', form);
  const passTypeCode = Form.useWatch('passTypeCode', form);

  const [routes, setRoutes] = useState<MonthlyPassRoute[]>([]);
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [submitting, setSubmitting] = useState(false);
  // Vé tháng vừa đăng ký thành công — null khi chưa đăng ký hoặc đang đăng ký vé mới.
  const [registered, setRegistered] = useState<MonthlyPass | null>(null);

  const loadRoutes = useCallback(async () => {
    try {
      setRoutes(await monthlyPassApi.listRoutes());
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không tải được danh sách tuyến.');
    } finally {
      setLoadingRoutes(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadRoutes();
  }, [loadRoutes]);

  const selectedRoute = routes.find((route) => route.id === routeId);
  const selectedPassType = findPassType(passTypeCode);

  // Ngày hiệu lực dự kiến: bắt đầu hôm nay, kết thúc sau đúng `durationMonths` tháng.
  const validity =
    selectedPassType === undefined
      ? null
      : {
          from: dayjs().startOf('day'),
          to: dayjs().startOf('day').add(selectedPassType.durationMonths, 'month'),
        };

  const handleSubmit = async ({ routeId: selectedRouteId, passTypeCode: selectedType }: RegistrationFormValues) => {
    setSubmitting(true);
    try {
      const pass = await monthlyPassApi.register({
        routeId: selectedRouteId,
        passTypeCode: selectedType,
      });
      setRegistered(pass);
      message.success('Đã đăng ký vé tháng thành công.');
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không thể đăng ký vé tháng.');
    } finally {
      setSubmitting(false);
    }
  };

  // Đăng ký thêm một vé khác: xoá kết quả cũ và đưa form về trạng thái ban đầu.
  const handleRegisterAnother = () => {
    setRegistered(null);
    form.resetFields();
  };

  const registeredRoute = routes.find((route) => route.id === registered?.routeId);
  const registeredType = findPassType(registered?.passTypeCode);

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Đăng ký vé tháng
        </Title>
        <Text type="secondary">
          Chọn tuyến và loại vé để xem giá, rồi đăng ký vé tháng tiết kiệm chi phí đi lại.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Form<RegistrationFormValues>
          form={form}
          layout="vertical"
          onFinish={handleSubmit}
        >
          <Space size="large" wrap align="start" style={{ display: 'flex', width: '100%' }}>
            <Form.Item
              name="routeId"
              label="Tuyến xe"
              style={{ flex: 1, minWidth: 260 }}
              rules={[{ required: true, message: 'Vui lòng chọn tuyến xe.' }]}
            >
              <Select
                placeholder="Chọn tuyến xe buýt"
                loading={loadingRoutes}
                showSearch
                optionFilterProp="label"
                options={routes.map((route) => ({
                  value: route.id,
                  label: `${route.code} — ${route.name}`,
                }))}
                suffixIcon={<EnvironmentOutlined />}
              />
            </Form.Item>

            <Form.Item
              name="passTypeCode"
              label="Loại vé"
              style={{ flex: 1, minWidth: 260 }}
              rules={[{ required: true, message: 'Vui lòng chọn loại vé.' }]}
            >
              <Select
                placeholder="Chọn loại vé tháng"
                options={PASS_TYPE_OPTIONS}
                suffixIcon={<IdcardOutlined />}
              />
            </Form.Item>
          </Space>

          {/* Ô "xem giá" — chỉ hiện khi đã chọn đủ cả tuyến lẫn loại vé. */}
          {selectedRoute && selectedPassType && validity && (
            <div
              style={{
                background: '#eef1ff',
                border: '1px solid #dbe1ff',
                borderRadius: 12,
                padding: '16px 20px',
                marginBottom: 24,
              }}
            >
              <Space direction="vertical" size={6} style={{ width: '100%' }}>
                <Text type="secondary" style={{ fontSize: 13 }}>
                  Giá vé tháng của bạn
                </Text>
                <Text style={{ fontSize: 28, fontWeight: 700, color: '#4361ee', lineHeight: 1.2 }}>
                  {formatVnd(selectedPassType.price)}
                </Text>
                <Text style={{ fontSize: 13 }}>
                  {selectedRoute.code} — {selectedRoute.name} · {selectedRoute.origin} →{' '}
                  {selectedRoute.destination}
                </Text>
                <Text type="secondary" style={{ fontSize: 13 }}>
                  Thời hạn {selectedPassType.durationMonths} tháng · Hiệu lực từ{' '}
                  {validity.from.format('DD/MM/YYYY')} đến {validity.to.format('DD/MM/YYYY')}
                </Text>
              </Space>
            </div>
          )}

          <Button
            type="primary"
            htmlType="submit"
            size="large"
            loading={submitting}
            disabled={!selectedRoute || !selectedPassType}
          >
            Đăng ký vé tháng
          </Button>
        </Form>
      </Card>

      {/* Trạng thái chưa có tuyến nào để chọn — khác với "chưa chọn" ở trên. */}
      {!loadingRoutes && routes.length === 0 && (
        <Empty
          style={{ marginTop: 40 }}
          description="Chưa có tuyến nào đang khai thác để đăng ký vé tháng."
        />
      )}

      {/* Kết quả đăng ký thành công. */}
      {registered && registeredRoute && registeredType && (
        <Card
          variant="borderless"
          style={{ borderRadius: 16, marginTop: 20, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
          title={
            <Space>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <span>Đăng ký vé tháng thành công</span>
            </Space>
          }
        >
          <Descriptions
            column={1}
            size="middle"
            items={[
              {
                key: 'code',
                label: 'Mã vé tháng',
                children: (
                  <Text code style={{ fontSize: 14 }}>
                    {registered.code}
                  </Text>
                ),
              },
              {
                key: 'route',
                label: 'Tuyến áp dụng',
                children: `${registeredRoute.code} — ${registeredRoute.name} (${registeredRoute.origin} → ${registeredRoute.destination})`,
              },
              {
                key: 'passType',
                label: 'Loại vé',
                children: registeredType.name,
              },
              {
                key: 'validity',
                label: 'Hiệu lực',
                children: `${dayjs(registered.validFrom).format('DD/MM/YYYY')} → ${dayjs(registered.validTo).format('DD/MM/YYYY')}`,
              },
              {
                key: 'price',
                label: 'Giá vé',
                children: <Text strong>{formatVnd(registered.price)}</Text>,
              },
              {
                key: 'status',
                label: 'Trạng thái',
                children: <MonthlyPassStatusTag pass={registered} />,
              },
            ]}
          />

          <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            Lưu lại mã vé tháng để soát vé khi lên xe. Vé tháng không đảm bảo có ghế ngồi.
          </Text>

          <Button style={{ marginTop: 16 }} onClick={handleRegisterAnother}>
            Đăng ký vé khác
          </Button>
        </Card>
      )}
    </div>
  );
}
