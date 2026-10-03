import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Form,
  InputNumber,
  Popconfirm,
  Row,
  Segmented,
  Select,
  Table,
  Tag,
  TimePicker,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { ThunderboltOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import { TRIP_STATUS_META, fetchRouteOptions } from '../api/tripApi';
import type { Trip, TripRouteOption, TripStatus } from '../api/tripApi';
import { fetchActiveBusOptions } from '../api/busApi';
import type { BusOption } from '../api/busApi';
import { generateTrips } from '../api/tripGenerateApi';
import type { GenerateTripsPayload, GenerateTripsResult } from '../api/tripGenerateApi';
import type { AppError } from '../api/axiosClient';

// -----------------------------------------------------------------------------
// Màn hình cấu hình tần suất chạy xe theo khung giờ trong ngày — story 13
// ("Là quản lý, tôi muốn thiết lập thời gian biểu và tần suất chạy xe cho từng tuyến
// theo ngày"), task Sprint 2 — Hoàng Văn Thịnh.
//
// Một lần cấu hình = một lần gọi POST /routes/{routeId}/trips/generate: chọn tuyến +
// ngày áp dụng + giờ bắt đầu/kết thúc + tần suất + xe → xem trước số chuyến sẽ sinh →
// bấm sinh. Khác màn hình "Quản lý lịch trình" (/trip-schedule — của Hạnh, thêm/sửa/huỷ
// TỪNG chuyến lẻ) ở chỗ đây là thao tác theo lô sinh cả dải chuyến cách đều nhau.
//
// Hợp đồng — docs/api-contract.md mục "Lịch trình chạy xe — /routes/{routeId}/trips",
// tiểu mục "POST .../trips/generate". Hai luật của hợp đồng được phản ánh ngay trên
// màn hình: tối đa 500 chuyến mỗi lần (khối xem trước chặn trước khi bấm) và thao tác
// nguyên tử — một chuyến trùng khung giờ thì không chuyến nào được tạo (ghi trong
// hộp xác nhận).
// -----------------------------------------------------------------------------

const { Title, Text } = Typography;

/** Múi giờ Việt Nam cố định — cùng lối TripSchedulePage / TripScheduleFormModal. */
const VIETNAM_OFFSET = '+07:00';

/** Trần số chuyến một lần sinh — hợp đồng ghi rõ "tối đa 500 chuyến", vượt là 400. */
const MAX_TRIPS_PER_CALL = 500;

/** Tần suất gợi ý nhanh (phút) — bấm một nút thay vì gõ số. */
const FREQUENCY_PRESETS = [10, 15, 20, 30, 60];

/**
 * Ghép ngày áp dụng + giờ trong ngày thành ISO 8601 kèm offset Việt Nam để gửi lên server.
 * Không dùng toISOString() — giờ gửi lên phải kèm múi giờ (hợp đồng), và người dùng nhìn
 * thấy đúng giờ mình chọn, không phụ thuộc múi giờ của trình duyệt người xem.
 */
function toVietnamIso(date: Dayjs, time: Dayjs): string {
  return `${date.format('YYYY-MM-DD')}T${time.format('HH:mm')}:00${VIETNAM_OFFSET}`;
}

/** Giá trị mặc định: hôm nay, khung 05:00–22:00, tần suất 15 phút — ví dụ của hợp đồng. */
function makeDefaultValues(): Partial<FrequencyFormValues> {
  const today = dayjs();
  return {
    date: today,
    startTime: today.hour(5).minute(0).second(0),
    endTime: today.hour(22).minute(0).second(0),
    frequencyMinutes: 15,
  };
}

/** Trạng thái khối "xem trước" — tính theo đúng luật sinh chuyến của hợp đồng. */
type PreviewState =
  | { kind: 'incomplete' }
  | { kind: 'invalid'; message: string }
  | { kind: 'over'; count: number }
  | { kind: 'ok'; count: number; first: string; last: string };

/**
 * Xem trước lịch trình sẽ sinh từ các giá trị đang hiển thị trên form.
 *
 * Công thức khớp hợp đồng: chuyến đầu đúng `startTime`, mỗi chuyến sau cách đều
 * `frequencyMinutes`, chuyến cuối KHÔNG vượt quá `endTime` → số chuyến =
 * floor(khoảng cách / tần suất) + 1. Ví dụ hợp đồng 05:00→22:00 tần suất 15 → 69 chuyến.
 */
function previewSchedule(
  date: Dayjs | undefined,
  start: Dayjs | undefined,
  end: Dayjs | undefined,
  frequency: number | null | undefined,
): PreviewState {
  if (!date || !start || !end || frequency === null || frequency === undefined) {
    return { kind: 'incomplete' };
  }

  // Khung giờ trong NGÀY: hai mốc chung một ngày áp dụng, giờ kết thúc phải sau giờ bắt
  // đầu — đúng luật 400 errors.endTime của backend, chặn trước ở đây cho dễ hiểu.
  const minutes = end.diff(start, 'minute');
  if (minutes <= 0) {
    return { kind: 'invalid', message: 'Giờ kết thúc phải sau giờ bắt đầu.' };
  }

  if (!Number.isInteger(frequency) || frequency < 1 || frequency > 1440) {
    return { kind: 'invalid', message: 'Tần suất phải là số nguyên từ 1 đến 1440 phút.' };
  }

  const count = Math.floor(minutes / frequency) + 1;
  if (count > MAX_TRIPS_PER_CALL) {
    return { kind: 'over', count };
  }

  const last = start.add((count - 1) * frequency, 'minute');
  return { kind: 'ok', count, first: start.format('HH:mm'), last: last.format('HH:mm') };
}

/** Giá trị các ô của form — tên ô đặt TRÙNG tên trường API để map lỗi server về thẳng ô. */
interface FrequencyFormValues {
  routeId: string;
  date: Dayjs;
  startTime: Dayjs;
  endTime: Dayjs;
  frequencyMinutes: number;
  busId: string;
}

export default function TripFrequencyPage() {
  const [form] = Form.useForm<FrequencyFormValues>();

  const [initialValues] = useState(makeDefaultValues);
  // Giá trị đang hiển thị trên form, giữ song song trong state để tính khối xem trước.
  // onValuesChange bắt mọi thay đổi người dùng; các chỗ đặt giá trị bằng code
  // (tuyến mặc định, nút tần suất gợi ý) tự cập nhật thêm vì setFieldValue không phát sự kiện.
  const [watched, setWatched] = useState<Partial<FrequencyFormValues>>(initialValues);

  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [buses, setBuses] = useState<BusOption[]>([]);
  const [loadingOptions, setLoadingOptions] = useState(true);

  const [submitting, setSubmitting] = useState(false);
  // Kết quả lần sinh gần nhất — giữ cả thông tin tuyến/ngày để tiêu đề khỏi mơ hồ.
  const [result, setResult] = useState<GenerateTripsResult | null>(null);
  const [resultInfo, setResultInfo] = useState<{ routeLabel: string; date: Dayjs } | null>(null);

  // Tải danh sách tuyến + xe đang khai thác cho hai ô chọn.
  const loadOptions = useCallback(async () => {
    try {
      const [routeList, busList] = await Promise.all([fetchRouteOptions(), fetchActiveBusOptions()]);
      setRoutes(routeList);
      setBuses(busList);

      // Tự chọn tuyến đầu tiên cho tiện — người dùng vẫn đổi được qua ô chọn.
      if (routeList[0]) {
        form.setFieldValue('routeId', routeList[0].id);
      }
    } catch (error) {
      message.error((error as Error).message || 'Không tải được dữ liệu ban đầu.');
    } finally {
      setLoadingOptions(false);
    }
  }, [form]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadOptions();
  }, [loadOptions]);

  const preview = previewSchedule(
    watched.date,
    watched.startTime,
    watched.endTime,
    watched.frequencyMinutes,
  );

  const applyFrequency = (value: number) => {
    form.setFieldValue('frequencyMinutes', value);
    setWatched((current) => ({ ...current, frequencyMinutes: value }));
  };

  const handleSubmit = async () => {
    let values: FrequencyFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — AntD đã hiển thị lỗi từng ô.
      return;
    }

    const route = routes.find((item) => item.id === values.routeId);
    const payload: GenerateTripsPayload = {
      busId: values.busId,
      startTime: toVietnamIso(values.date, values.startTime),
      endTime: toVietnamIso(values.date, values.endTime),
      frequencyMinutes: values.frequencyMinutes,
    };

    setSubmitting(true);
    try {
      const generated = await generateTrips(values.routeId, payload);
      setResult(generated);
      setResultInfo({
        routeLabel: route ? `${route.code} — ${route.name}` : '',
        date: values.date,
      });
      message.success(`Đã sinh ${generated.total} chuyến.`);
    } catch (error) {
      const appError = error as AppError;
      if (appError.errors) {
        // Lỗi theo từng trường (endTime, frequencyMinutes, busId…) → gắn thẳng vào ô input;
        // tên ô trùng tên trường API nên map thẳng, không cần bảng dịch.
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof FrequencyFormValues,
            errors: fieldErrors,
          })),
        );
      } else {
        // 409 trùng khung giờ (không chuyến nào được tạo), 404 tuyến/xe, 403… — hiện toast.
        message.error(appError.customMessage || 'Sinh chuyến thất bại.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const columns: TableProps<Trip>['columns'] = [
    {
      title: 'Khởi hành',
      dataIndex: 'departureTime',
      key: 'departureTime',
      width: 160,
      render: (departureTime: string) => (
        <span style={{ fontWeight: 600 }}>{dayjs(departureTime).format('DD/MM/YYYY HH:mm')}</span>
      ),
    },
    {
      title: 'Giờ đến',
      dataIndex: 'arrivalTime',
      key: 'arrivalTime',
      width: 100,
      render: (arrivalTime: string | null) =>
        arrivalTime ? dayjs(arrivalTime).format('HH:mm') : '—',
    },
    {
      title: 'Biển số xe',
      dataIndex: 'busLicensePlate',
      key: 'busLicensePlate',
      width: 140,
      render: (plate: string) => (plate ? <Tag>{plate}</Tag> : '—'),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (tripStatus: TripStatus) => {
        // Backend chỉ trả bốn mã trong TRIP_STATUS_META; giá trị lạ thì hiện nguyên văn,
        // không để màn hình vỡ vì tra meta không thấy — cùng lối TripSchedulePage.
        const meta = TRIP_STATUS_META[tripStatus];
        return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{tripStatus}</Tag>;
      },
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Cấu hình tần suất chạy xe
        </Title>
        <Text type="secondary">
          Chọn tuyến, ngày áp dụng, khung giờ và tần suất — hệ thống sinh các chuyến cách đều
          nhau trong khung giờ đó.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        {!loadingOptions && routes.length === 0 ? (
          <Empty description="Chưa có tuyến nào để cấu hình tần suất." />
        ) : (
          <>
            {!loadingOptions && buses.length === 0 && (
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 16 }}
                message="Chưa có xe nào đang khai thác — cần ít nhất một xe ở trạng thái Đang khai thác để sinh chuyến."
              />
            )}

            <Form
              form={form}
              layout="vertical"
              initialValues={initialValues}
              onValuesChange={(_, allValues) => setWatched(allValues)}
            >
              <Row gutter={16}>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="routeId"
                    label="Tuyến"
                    rules={[{ required: true, message: 'Vui lòng chọn tuyến' }]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="label"
                      placeholder="Chọn tuyến"
                      loading={loadingOptions}
                      options={routes.map((route) => ({
                        value: route.id,
                        label: `${route.code} — ${route.name}`,
                      }))}
                    />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="date"
                    label="Ngày áp dụng"
                    rules={[{ required: true, message: 'Vui lòng chọn ngày áp dụng' }]}
                  >
                    <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="busId"
                    label="Xe buýt"
                    rules={[{ required: true, message: 'Vui lòng chọn xe buýt' }]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="label"
                      placeholder="Chọn xe đang khai thác"
                      notFoundContent="Không có xe nào đang khai thác"
                      options={buses.map((bus) => ({
                        value: bus.id,
                        label: `${bus.licensePlate} · ${bus.busType} · ${bus.capacity} chỗ`,
                      }))}
                    />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} md={6}>
                  <Form.Item
                    name="startTime"
                    label="Giờ bắt đầu"
                    rules={[{ required: true, message: 'Vui lòng chọn giờ bắt đầu' }]}
                  >
                    <TimePicker format="HH:mm" style={{ width: '100%' }} placeholder="05:00" />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="endTime"
                    label="Giờ kết thúc"
                    dependencies={['startTime']}
                    rules={[
                      { required: true, message: 'Vui lòng chọn giờ kết thúc' },
                      {
                        validator: (_, value: Dayjs | undefined) => {
                          if (!value) return Promise.resolve();
                          const start = form.getFieldValue('startTime') as Dayjs | undefined;
                          if (start && !value.isAfter(start)) {
                            return Promise.reject(
                              new Error('Giờ kết thúc phải sau giờ bắt đầu'),
                            );
                          }
                          return Promise.resolve();
                        },
                      },
                    ]}
                  >
                    <TimePicker format="HH:mm" style={{ width: '100%' }} placeholder="22:00" />
                  </Form.Item>
                </Col>

                <Col xs={24} md={12}>
                  <Form.Item
                    name="frequencyMinutes"
                    label="Tần suất (khoảng cách giữa hai chuyến liên tiếp)"
                    rules={[
                      { required: true, message: 'Vui lòng nhập tần suất' },
                      {
                        type: 'integer',
                        min: 1,
                        max: 1440,
                        message: 'Tần suất phải là số nguyên từ 1 đến 1440 phút',
                      },
                    ]}
                    extra={
                      <Segmented
                        size="small"
                        options={FREQUENCY_PRESETS.map((minutes) => ({
                          label: `${minutes} phút`,
                          value: minutes,
                        }))}
                        value={
                          FREQUENCY_PRESETS.includes(watched.frequencyMinutes ?? 0)
                            ? watched.frequencyMinutes
                            : undefined
                        }
                        onChange={(value) => applyFrequency(value as number)}
                      />
                    }
                  >
                    <InputNumber
                      min={1}
                      max={1440}
                      style={{ width: '100%' }}
                      addonAfter="phút / chuyến"
                    />
                  </Form.Item>
                </Col>
              </Row>
            </Form>

            <div style={{ marginTop: 8 }}>
              {preview.kind === 'incomplete' && (
                <Alert
                  type="info"
                  showIcon
                  message="Chọn đủ ngày, khung giờ và tần suất để xem trước số chuyến sẽ sinh."
                />
              )}
              {preview.kind === 'invalid' && (
                <Alert type="error" showIcon message={preview.message} />
              )}
              {preview.kind === 'over' && (
                <Alert
                  type="warning"
                  showIcon
                  message={`Dự kiến ${preview.count} chuyến — vượt trần ${MAX_TRIPS_PER_CALL} chuyến mỗi lần sinh.`}
                  description="Thu hẹp khung giờ hoặc tăng tần suất rồi thử lại."
                />
              )}
              {preview.kind === 'ok' && (
                <Alert
                  type="success"
                  showIcon
                  message={`Dự kiến sinh ${preview.count} chuyến`}
                  description={`Chuyến đầu ${preview.first}, chuyến cuối ${preview.last}, cách nhau mỗi ${watched.frequencyMinutes} phút.`}
                />
              )}
            </div>

            <div style={{ marginTop: 16 }}>
              <Popconfirm
                title={preview.kind === 'ok' ? `Sinh ${preview.count} chuyến?` : 'Sinh chuyến?'}
                description="Nếu một chuyến trong dải trùng khung giờ với chuyến đã có, KHÔNG chuyến nào được tạo — thao tác nguyên tử."
                okText="Sinh chuyến"
                cancelText="Xem lại"
                onConfirm={handleSubmit}
                disabled={preview.kind !== 'ok'}
              >
                <Button
                  type="primary"
                  icon={<ThunderboltOutlined />}
                  loading={submitting}
                  disabled={preview.kind !== 'ok'}
                >
                  {preview.kind === 'ok' ? `Sinh ${preview.count} chuyến` : 'Sinh chuyến'}
                </Button>
              </Popconfirm>
            </div>
          </>
        )}
      </Card>

      {result && resultInfo && (
        <Card
          variant="borderless"
          style={{
            marginTop: 24,
            borderRadius: 16,
            boxShadow: '0 4px 12px rgba(0,0,0,0.03)',
          }}
          title={`Đã sinh ${result.total} chuyến`}
          extra={
            <Text type="secondary">
              {resultInfo.routeLabel ? `${resultInfo.routeLabel} · ` : ''}
              ngày {resultInfo.date.format('DD/MM/YYYY')}
            </Text>
          }
        >
          <Table<Trip>
            rowKey="id"
            columns={columns}
            dataSource={result.items}
            scroll={{ x: 600 }}
            pagination={{
              pageSize: 20,
              showSizeChanger: false,
              showTotal: (total, range) => `Hiển thị ${range[0]}–${range[1]} trên ${total} chuyến`,
            }}
          />
        </Card>
      )}
    </div>
  );
}
