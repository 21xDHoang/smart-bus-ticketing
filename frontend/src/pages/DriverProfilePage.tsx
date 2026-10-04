import { useEffect, useState } from 'react';
import { Button, Card, Input, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { ReloadOutlined } from '@ant-design/icons';
import { DRIVER_TRIP_PAGE_SIZE, fetchDrivers, fetchDriverTrips } from '../api/driverApi';
import type { Driver } from '../api/driverApi';
import type { AppError } from '../api/axiosClient';
import {
  SHIFT_STATE_META,
  deriveShiftSummary,
  describeShift,
  vietnamDayRange,
} from '../components/driverShift';
import type { ShiftSummary } from '../components/driverShift';
import DriverShiftModal from '../components/DriverShiftModal';

const { Title, Text } = Typography;

// -----------------------------------------------------------------------------
// Màn hình hồ sơ tài xế + trạng thái ca làm việc — story 14, task Sprint 2 dòng 21,
// Hoàng Văn Thịnh. Chỉ đọc: thêm/sửa/khoá tài xế là việc của màn hình quản trị tài khoản.
//
// Hệ thống không có phụ xe (quy ước A8.4), nên màn hình chỉ có tài xế.
//
// Về cột "Ca hôm nay": hợp đồng KHÔNG có endpoint trả trạng thái ca cho cả danh sách, chỉ có
// GET /drivers/{id}/trips cho TỪNG tài xế. Nên màn hình gọi song song cho các tài xế ĐANG
// HIỆN TRÊN TRANG (tối đa bằng pageSize), không gọi cho toàn bộ kết quả lọc — lọc ra 300
// tài xế mà gọi 300 lượt thì đổi một bộ lọc là đủ sập.
//
// Đây là điểm nợ kỹ thuật đã biết: cách đúng là bổ sung một trường `shiftState` vào
// DriverListResponse để lấy kèm trong một lượt. Việc đó ĐỔI HÌNH DẠNG API, mà quy ước cấm tự
// đổi — phải sửa docs/api-contract.md trước rồi báo lại nhóm. Chưa làm ở đây.
// -----------------------------------------------------------------------------

const DriverProfilePage = () => {
  const [search, setSearch] = useState('');
  const [isActive, setIsActive] = useState<boolean>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<Driver[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách khi bấm "Làm mới" hoặc sau khi đóng modal.
  const [reloadKey, setReloadKey] = useState(0);

  /**
   * Ca làm việc hôm nay của từng tài xế đang hiện trên trang.
   *
   * Dùng Map chứ không dùng object: `map.get(id)` trả về `undefined` khi chưa tính xong và
   * `null` khi tính lỗi — hai tình huống cần hiện hai câu khác nhau, mà tra object thì cả hai
   * đều ra `undefined` nên không phân biệt được.
   */
  const [shifts, setShifts] = useState<Map<string, ShiftSummary | null>>(() => new Map());
  const [shiftsLoading, setShiftsLoading] = useState(false);

  const [selectedDriver, setSelectedDriver] = useState<Driver | null>(null);

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải thật từ API (không phải "đồng bộ state dẫn xuất") nên tắt
    // cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchDrivers({ page, pageSize, search: search || undefined, isActive })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        message.error((err as AppError).customMessage || 'Không thể tải danh sách tài xế.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, isActive, page, pageSize, reloadKey]);

  // Tính ca làm việc cho đúng những tài xế vừa tải xong. Chạy theo `data` nên tự chạy lại mỗi
  // khi danh sách đổi (đổi trang, đổi bộ lọc, bấm Làm mới).
  useEffect(() => {
    // Danh sách rỗng thì không có gì để tính, nhưng vẫn phải hạ cờ tải: lượt tải chạy ngay
    // trước đó đã bị cleanup đặt `cancelled = true` nên `finally` của nó thoát sớm, để nguyên
    // là cờ kẹt ở true và bảng hiện "Đang tính…" mãi.
    //
    // Không xoá `shifts` khi rỗng: bảng không còn dòng nào để đọc nó, và giữ lại thì lượt tải
    // kế tiếp vẫn thay bằng Map mới dựng từ đúng danh sách mới.
    // oxlint-disable-next-line react/set-state-in-effect
    setShiftsLoading(data.length > 0);

    if (data.length === 0) return;

    let cancelled = false;

    const now = dayjs();
    const { from, to } = vietnamDayRange(now);

    Promise.all(
      data.map(async (driver): Promise<readonly [string, ShiftSummary | null]> => {
        try {
          const result = await fetchDriverTrips(driver.id, {
            from,
            to,
            page: 1,
            pageSize: DRIVER_TRIP_PAGE_SIZE,
          });
          return [driver.id, deriveShiftSummary(result.items, now)];
        } catch {
          // Một tài xế lỗi không được làm trắng cả bảng — riêng dòng đó hiện "Không tải được".
          return [driver.id, null];
        }
      }),
    )
      .then((entries) => {
        if (cancelled) return;
        const next = new Map<string, ShiftSummary | null>();
        for (const [driverId, summary] of entries) next.set(driverId, summary);
        setShifts(next);
      })
      .finally(() => {
        if (!cancelled) setShiftsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [data]);

  const handleRefresh = () => {
    setSelectedDriver(null);
    setReloadKey((key) => key + 1);
  };

  const renderShift = (driver: Driver) => {
    if (shiftsLoading && !shifts.has(driver.id)) {
      return (
        <Text type="secondary" style={{ fontSize: 12 }}>
          Đang tính…
        </Text>
      );
    }

    const summary = shifts.get(driver.id);

    if (summary === undefined) return <Text type="secondary">—</Text>;
    if (summary === null) {
      return (
        <Text type="secondary" style={{ fontSize: 12 }}>
          Không tải được
        </Text>
      );
    }

    const meta = SHIFT_STATE_META[summary.state];

    return (
      <Space direction="vertical" size={2}>
        <Tag color={meta.color}>{meta.label}</Tag>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {describeShift(summary)}
        </Text>
      </Space>
    );
  };

  // Khai báo trong component (không để ở module như BusManagePage) vì cột "Ca hôm nay" chen
  // giữa bảng và phải đọc `shifts`.
  const columns: TableProps<Driver>['columns'] = [
    {
      title: 'Tài xế',
      dataIndex: 'fullName',
      key: 'fullName',
      width: 220,
      render: (fullName: string, driver) => (
        <Space direction="vertical" size={0}>
          <Text strong>{fullName}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {driver.phoneNumber}
          </Text>
        </Space>
      ),
    },
    {
      title: 'Email',
      dataIndex: 'email',
      key: 'email',
      width: 200,
      render: (email: string | null) => email || <Text type="secondary">Chưa có</Text>,
    },
    {
      title: 'Ca hôm nay',
      key: 'shift',
      width: 220,
      render: (_, driver) => renderShift(driver),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY'),
    },
    {
      title: 'Tài khoản',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 120,
      render: (isActive: boolean) =>
        isActive ? <Tag color="success">Đang làm</Tag> : <Tag color="default">Đã khoá</Tag>,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 100,
      render: (_, driver) => (
        <Button type="link" size="small" onClick={() => setSelectedDriver(driver)}>
          Xem ca
        </Button>
      ),
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Hồ sơ tài xế
        </Title>
        <Text type="secondary">
          Danh sách tài xế và ca làm việc hôm nay của từng người.
        </Text>
      </div>

      <Card variant="borderless" style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}>
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo tên hoặc số điện thoại"
            style={{ width: 280 }}
            onSearch={(value) => {
              setSearch(value.trim());
              setPage(1);
            }}
          />

          <Select
            allowClear
            placeholder="Trạng thái tài khoản"
            style={{ width: 200 }}
            value={isActive}
            onChange={(value) => {
              setIsActive(value);
              setPage(1);
            }}
            options={[
              { value: true, label: 'Đang làm' },
              { value: false, label: 'Đã khoá' },
            ]}
          />

          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
        </Space>

        <Table<Driver>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 980 }}
          locale={{ emptyText: 'Không tìm thấy tài xế nào' }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showTotal: (count, range) => `Hiển thị ${range[0]}–${range[1]} trên ${count} tài xế`,
            onChange: (nextPage, nextPageSize) => {
              setPage(nextPageSize === pageSize ? nextPage : 1);
              setPageSize(nextPageSize);
            },
          }}
        />
      </Card>

      <DriverShiftModal
        open={selectedDriver !== null}
        driver={selectedDriver}
        onClose={() => setSelectedDriver(null)}
      />
    </div>
  );
};

export default DriverProfilePage;
