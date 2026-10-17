import { useEffect, useState } from 'react';
import {
  Button,
  Checkbox,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { assignTripDriver, fetchBuses, BUS_STATUS_META } from '../api/busApi';
import type { Bus, BusStatus, BusTripSummary } from '../api/busApi';
import busCrudApi, { BUS_STATUS_OPTIONS } from '../api/busCrudApi';
import type { BusPayload, UpdateBusPayload } from '../api/busCrudApi';
import { fetchDriverOptions } from '../api/tripAssignmentApi';
import type { DriverOption } from '../api/tripAssignmentApi';
import type { AppError } from '../api/axiosClient';
import BusFormModal from '../components/BusFormModal';
import { FilterBar, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDate, formatTime } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';

// Màn hình quản lý đội xe (story 14): bảng + tìm kiếm + lọc trạng thái + phân trang.
// Phần Thêm/Sửa xe + xác nhận ngừng khai thác (nút, modal, popconfirm) là task "Màn hình
// quản lý đội xe" của Dương Thị Hạnh, gắn vào đây dùng chung một trang.

/**
 * Cột "Tuyến được phân công": mã các tuyến xe đang chạy hoặc sẽ chạy trong 7 ngày tới.
 *
 * Hệ thống không có bảng gán xe ↔ tuyến — backend suy ra từ các chuyến (xem docs/api-contract.md
 * mục "Xe buýt — /buses"). Vì vậy đây là danh sách tuyến THEO CHUYẾN, không phải một trường cố
 * định trên xe; tuyến chỉ hiện khi có chuyến Running hoặc Scheduled trong cửa sổ 7 ngày.
 */
const renderAssignedRoutes = (_: unknown, bus: Bus) => {
  if (bus.assignedRoutes.length === 0) {
    return <Typography.Text type="secondary">Chưa xếp chuyến</Typography.Text>;
  }

  // Hai tuyến đầu là đủ để nhận biết; phần còn lại gom vào "+N" có tooltip — cột không
  // cao lên theo số tuyến và bảng vẫn đọc được khi xe chạy nhiều tuyến.
  const shown = bus.assignedRoutes.slice(0, 2);
  const hidden = bus.assignedRoutes.slice(2);

  return (
    <Space size={4} wrap>
      {shown.map((route) => (
        <StatusTag
          key={route.routeId}
          color="blue"
          title={`${route.routeName} — ${route.tripCount} chuyến trong 7 ngày`}
        >
          {route.routeCode}
        </StatusTag>
      ))}
      {hidden.length > 0 && (
        <StatusTag
          title={hidden.map((route) => `${route.routeCode}: ${route.routeName}`).join('\n')}
        >
          +{hidden.length}
        </StatusTag>
      )}
    </Space>
  );
};

/**
 * Cột "Chuyến gần nhất": ưu tiên chuyến ĐANG CHẠY, không có thì hiện chuyến kế tiếp.
 *
 * Tách khỏi cột "Tuyến" vì hai câu hỏi khác nhau: tuyến nào (phân công) và chuyến nào
 * (điều độ). Một xe có thể đang chạy tuyến 01 nhưng chuyến kế tiếp lại là tuyến B10.
 */
const renderTrip = (_: unknown, bus: Bus) => {
  if (bus.currentTrip) {
    return (
      <Space direction="vertical" size={0}>
        <Space size={4}>
          <StatusTag color="success">Đang chạy</StatusTag>
          <span>{bus.currentTrip.routeCode}</span>
        </Space>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {bus.currentTrip.currentStopName
            ? `Đã qua ${bus.currentTrip.currentStopName}`
            : 'Chưa báo vị trí'}
        </Typography.Text>
      </Space>
    );
  }

  if (bus.nextTrip) {
    return (
      <Space direction="vertical" size={0}>
        <span>
          <strong>{bus.nextTrip.routeCode}</strong> · {formatTime(bus.nextTrip.departureTime)}{' '}
          {formatDate(bus.nextTrip.departureTime)}
        </span>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {bus.nextTrip.routeName}
        </Typography.Text>
      </Space>
    );
  }

  return <Typography.Text type="secondary">Chưa có chuyến trong 7 ngày</Typography.Text>;
};

/** Cột "Tài xế": tài xế của chuyến đang chạy, không có thì của chuyến kế tiếp. */
const renderDriver = (_: unknown, bus: Bus) => {
  const trip = bus.currentTrip ?? bus.nextTrip;

  if (!trip) {
    return <Typography.Text type="secondary">—</Typography.Text>;
  }

  return trip.driverName ? (
    <span>{trip.driverName}</span>
  ) : (
    <Typography.Text type="secondary">Chưa phân công</Typography.Text>
  );
};

/**
 * Chuyến của xe mà màn hình này phân công được: chuyến đang chạy và chuyến kế tiếp trong
 * cửa sổ 7 ngày — đúng hai chuyến backend trả về trong `BusResponse`.
 *
 * Hai chuyến có thể thuộc HAI tuyến khác nhau (xe chạy nối tuyến), nên modal cho chọn từng
 * chuyến thay vì mặc định gán cho tất cả.
 */
const assignableTripsOf = (bus: Bus): BusTripSummary[] =>
  [bus.currentTrip, bus.nextTrip].filter((trip): trip is BusTripSummary => trip !== null);

/** Nhãn một chuyến trong modal phân công — đủ tuyến, giờ và tên tuyến để không chọn nhầm. */
const tripOptionLabel = (trip: BusTripSummary) =>
  `${trip.status === 'Running' ? 'Đang chạy' : 'Chuyến sắp chạy'} · ${trip.routeCode} · ` +
  `${formatTime(trip.departureTime)} ${formatDate(trip.departureTime)} — ${trip.routeName}`;

/** Xe đã có tài xế ở chuyến đang hiển thị hay chưa — quyết định nhãn "Phân công" / "Đổi". */
const hasAssignedDriver = (bus: Bus) =>
  Boolean((bus.currentTrip ?? bus.nextTrip)?.driverId);

/**
 * Cột "Ghế đã sinh": số ghế THẬT so với sức chứa khai báo. Lệch nghĩa là xe chưa cấu hình
 * sơ đồ ghế (hoặc sơ đồ vừa đổi) — đánh dấu đỏ để quản lý thấy ngay thay vì phát hiện lúc
 * hành khách không chọn được ghế.
 */
const renderSeats = (_: unknown, bus: Bus) => {
  const { seatsCreated, capacity, isComplete, layout } = bus.seatSummary;
  const text = `${seatsCreated}/${capacity} ghế`;
  const title = layout
    ? `Sơ đồ: ${layout.numberOfFloors} tầng · ${layout.rowsPerFloor} hàng × ${layout.columnsPerRow} cột · ${layout.vipSeatCount} ghế VIP`
    : 'Loại xe chưa cấu hình sơ đồ ghế';

  return isComplete ? (
    <span title={title}>{text}</span>
  ) : (
    <Typography.Text type="danger" title={title}>
      {text}
    </Typography.Text>
  );
};

/**
 * Cột cơ bản của bảng. Nhận callback phân công tài xế từ trang (cột "Tài xế" cần gọi state +
 * modal của component), thay vì để cột ở module như trước.
 */
const buildBaseColumns = (
  onAssignDriver: (bus: Bus) => void,
): NonNullable<TableProps<Bus>['columns']> => [
  {
    title: 'Biển số',
    dataIndex: 'licensePlate',
    key: 'licensePlate',
    width: 130,
    render: (licensePlate: string) => <StatusTag color="blue">{licensePlate}</StatusTag>,
  },
  {
    title: 'Loại xe',
    dataIndex: 'busType',
    key: 'busType',
    width: 180,
  },
  {
    title: 'Sức chứa',
    dataIndex: 'capacity',
    key: 'capacity',
    width: 110,
    render: (capacity: number) => `${capacity} chỗ`,
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    key: 'status',
    width: 170,
    render: (status: BusStatus) => {
      // Backend chỉ trả 'Active' | 'Maintenance' | 'Inactive'. Lỡ có giá trị lạ thì hiện
      // nguyên văn, không để màn hình vỡ vì tra meta không thấy.
      const meta = BUS_STATUS_META[status];
      return meta ? <StatusTag color={meta.color}>{meta.label}</StatusTag> : <StatusTag>{status}</StatusTag>;
    },
  },
  {
    title: 'Tuyến được phân công',
    key: 'assignedRoutes',
    width: 200,
    render: renderAssignedRoutes,
  },
  {
    title: 'Đang chạy / chuyến kế tiếp',
    key: 'trip',
    width: 240,
    render: renderTrip,
  },
  {
    title: 'Tài xế',
    key: 'driver',
    width: 210,
    render: (_, bus) => (
      <Space direction="vertical" size={2}>
        {renderDriver(_, bus)}
        <Button
          type="link"
          size="small"
          icon={<EditOutlined />}
          style={{ padding: 0, height: 'auto' }}
          disabled={assignableTripsOf(bus).length === 0}
          title={
            assignableTripsOf(bus).length === 0
              ? 'Xe chưa có chuyến trong 7 ngày nên chưa phân công được'
              : undefined
          }
          onClick={() => onAssignDriver(bus)}
        >
          {hasAssignedDriver(bus) ? 'Đổi tài xế' : 'Phân công tài xế'}
        </Button>
      </Space>
    ),
  },
  {
    title: 'Ghế đã sinh',
    key: 'seats',
    width: 130,
    render: renderSeats,
  },
  {
    title: 'Ngày tạo',
    dataIndex: 'createdAt',
    key: 'createdAt',
    width: 130,
    render: (createdAt: string) => formatDate(createdAt),
  },
];

const BusManagePage = () => {
  const [search, setSearch] = useState('');
  // Mặc định chỉ hiện xe ĐANG khai thác: "xoá" (ngừng khai thác) xong xe biến mất khỏi danh
  // sách, khớp kỳ vọng "xoá = không còn thấy". Muốn xem xe đã ngừng thì chọn bộ lọc.
  const [status, setStatus] = useState<BusStatus>('Active');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<Bus[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/xoá thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Bus | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Phân công tài xế ngay trên bảng: chọn chuyến (đang chạy / kế tiếp) + tài xế.
  const [assigningBus, setAssigningBus] = useState<Bus | null>(null);
  const [assigningTripIds, setAssigningTripIds] = useState<string[]>([]);
  const [assigningDriverId, setAssigningDriverId] = useState<string>();
  const [driverOptions, setDriverOptions] = useState<DriverOption[]>([]);
  const [loadingDrivers, setLoadingDrivers] = useState(false);
  const [savingDriver, setSavingDriver] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (bus: Bus) => {
    setEditing(bus);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: BusPayload | UpdateBusPayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await busCrudApi.update(id, payload as UpdateBusPayload);
        message.success('Đã cập nhật xe buýt.');
      } else {
        await busCrudApi.create(payload);
        message.success('Đã thêm xe buýt.');
      }
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      // Có lỗi theo từng trường thì để modal gắn vào ô input; ngược lại mới hiện toast.
      if (!appError.errors) {
        message.error(appError.customMessage || 'Thao tác thất bại.');
      }
      throw error;
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await busCrudApi.remove(id);
      message.success('Đã ngừng khai thác xe buýt.');
      // Xoá dòng cuối của trang cuối thì lùi về trang trước — tránh đứng ở trang rỗng (400).
      if (data.length === 1 && page > 1) {
        setPage(page - 1);
      } else {
        setReloadKey((key) => key + 1);
      }
    } catch (error) {
      message.error((error as AppError).customMessage || 'Thao tác thất bại.');
    }
  };

  /**
   * Mở modal phân công: mặc định chọn chuyến ưu tiên (đang chạy, không có thì chuyến kế tiếp)
   * và nạp đúng tài xế hiện tại của chuyến đó.
   *
   * Danh sách tài xế tải lại MỖI LẦN mở để tài khoản vừa bị khoá / vừa mở hoạt động lại hiện
   * đúng — bộ chọn tài xế là nơi sai một dòng sẽ phân công nhầm người.
   */
  const openAssignDriver = (bus: Bus) => {
    const trips = assignableTripsOf(bus);
    if (trips.length === 0) return;

    setAssigningBus(bus);
    setAssigningTripIds([trips[0].tripId]);
    setAssigningDriverId(trips[0].driverId ?? undefined);
    setLoadingDrivers(true);
    fetchDriverOptions()
      .then((drivers) => {
        // Tài xế hiện tại có thể đã bị khoá nên không nằm trong danh sách đang hoạt động.
        // Thêm lại để ô chọn hiện TÊN thay vì GUID; nếu thật sự bị khoá, lúc lưu backend trả
        // 409 kèm thông báo rõ ràng.
        const current = trips[0];
        const missingCurrentDriver =
          current.driverId !== null &&
          current.driverName !== null &&
          !drivers.some((driver) => driver.id === current.driverId);

        setDriverOptions(
          missingCurrentDriver
            ? [
                ...drivers,
                {
                  id: current.driverId!,
                  fullName: `${current.driverName} (tạm khoá)`,
                  phoneNumber: '',
                },
              ]
            : drivers,
        );
      })
      .catch(() => message.error('Không tải được danh sách tài xế.'))
      .finally(() => setLoadingDrivers(false));
  };

  /**
   * Lưu phân công cho từng chuyến đã chọn. Gọi PATCH /trips/{id}/assignment — API này chỉ đổi
   * một chuyến, nên chọn hai chuyến là hai lời gọi; dừng ở lỗi đầu tiên và báo rõ đã lưu được
   * bao nhiêu chuyến để người dùng không tưởng cả lô đã xong.
   *
   * Trùng lịch KHÔNG phải lỗi: backend vẫn lưu và trả danh sách chuyến trùng trong `conflicts`.
   * Hiện cảnh báo riêng thay vì im lặng, vì người điều hành cần biết mình vừa xếp chồng lịch.
   */
  const handleAssignDriver = async () => {
    if (!assigningBus || !assigningDriverId || assigningTripIds.length === 0) return;

    const driverName =
      driverOptions.find((driver) => driver.id === assigningDriverId)?.fullName ?? 'tài xế';
    const conflictingTripIds = new Set<string>();
    let assignedCount = 0;
    let errorMessage: string | undefined;

    setSavingDriver(true);
    try {
      for (const tripId of assigningTripIds) {
        try {
          const result = await assignTripDriver(tripId, assigningDriverId);
          assignedCount += 1;
          result.conflicts.driverConflicts.forEach((trip) => conflictingTripIds.add(trip.id));
        } catch (error) {
          errorMessage = (error as AppError).customMessage || 'Phân công tài xế thất bại.';
          break;
        }
      }
    } finally {
      setSavingDriver(false);
    }

    if (assignedCount > 0) {
      message.success(`Đã phân công ${driverName} cho ${assignedCount} chuyến.`);
      setAssigningBus(null);
      setReloadKey((key) => key + 1);
    }

    if (errorMessage) {
      message.error(errorMessage);
    } else if (conflictingTripIds.size > 0) {
      message.warning(
        `${driverName} đang trùng lịch với ${conflictingTripIds.size} chuyến khác — phân công vẫn được lưu.`,
      );
    }
  };

  // Cột "Hành động" cần gọi openEdit/handleDelete nên ghép ở đây, không để ở module.
  const columns: TableProps<Bus>['columns'] = [
    ...buildBaseColumns(openAssignDriver),
    {
      title: 'Hành động',
      key: 'actions',
      width: 180,
      render: (_, bus) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(bus)}>
            Sửa
          </Button>
          <Popconfirm
            title="Ngừng khai thác xe?"
            description={`Xe “${bus.licensePlate}” sẽ chuyển sang trạng thái ngừng khai thác.`}
            okText="Ngừng"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(bus.id)}
          >
            <Button type="link" size="small" danger>
              Ngừng khai thác
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchBuses({ page, pageSize, search: search || undefined, status })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách đội xe.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, status, page, pageSize, reloadKey]);

  return (
    <div>
      <PageHeader
        title="Quản lý đội xe"
        subtitle="Tra cứu xe theo biển số, loại xe và trạng thái — kèm tuyến đang phụ trách, chuyến kế tiếp và tình trạng ghế."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                Thêm xe
              </Button>
            }
          >
            <Input.Search
              allowClear
              placeholder="Tìm theo biển số hoặc loại xe"
              style={{ width: 280 }}
              onSearch={(value) => {
                setSearch(value.trim());
                setPage(1);
              }}
              onChange={(e) => {
                // Bấm nút X (allowClear) thì cập nhật ngay, không cần chờ Enter.
                if (!e.target.value) {
                  setSearch('');
                  setPage(1);
                }
              }}
            />

            <Select<BusStatus>
              allowClear
              // Hiện trạng thái đang lọc để bộ lọc mặc định "Đang khai thác" không bị ẩn —
              // không có dòng này thì danh sách đã lọc mà ô chọn trông như chưa chọn gì.
              value={status}
              placeholder="Trạng thái"
              style={{ width: 200 }}
              options={BUS_STATUS_OPTIONS}
              onChange={(value) => {
                setStatus(value ?? undefined);
                setPage(1);
              }}
            />
          </FilterBar>
        }
      >

        <Table<Bus>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 1600 }}
          locale={{ emptyText: 'Không tìm thấy xe buýt nào' }}
          pagination={tablePagination({
            page,
            pageSize,
            total,
            unitLabel: 'xe',
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          })}
        />
      </PageCard>

      <BusFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />

      <Modal
        open={assigningBus !== null}
        title={
          assigningBus
            ? `Phân công tài xế — ${assigningBus.licensePlate}`
            : 'Phân công tài xế'
        }
        okText="Lưu phân công"
        cancelText="Huỷ"
        confirmLoading={savingDriver}
        okButtonProps={{
          disabled: assigningTripIds.length === 0 || !assigningDriverId,
        }}
        onOk={handleAssignDriver}
        onCancel={() => setAssigningBus(null)}
        destroyOnHidden
      >
        {assigningBus && (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <div>
              <Typography.Text strong>Áp dụng cho chuyến</Typography.Text>
              <Checkbox.Group
                value={assigningTripIds}
                onChange={(values) => {
                  const ids = values as string[];
                  setAssigningTripIds(ids);
                  // Đổi chuyến đang chọn → nạp tài xế hiện tại của chuyến đầu tiên, tránh
                  // giữ nguyên lựa chọn của chuyến vừa bỏ chọn.
                  const firstTrip = assignableTripsOf(assigningBus).find(
                    (trip) => trip.tripId === ids[0],
                  );
                  setAssigningDriverId(firstTrip?.driverId ?? undefined);
                }}
                style={{ display: 'block', marginTop: 8 }}
              >
                <Space direction="vertical" size={8}>
                  {assignableTripsOf(assigningBus).map((trip) => (
                    <Checkbox key={trip.tripId} value={trip.tripId}>
                      {tripOptionLabel(trip)}
                    </Checkbox>
                  ))}
                </Space>
              </Checkbox.Group>
            </div>

            <div>
              <Typography.Text strong>Tài xế phụ trách</Typography.Text>
              <Select
                showSearch
                style={{ width: '100%', marginTop: 8 }}
                placeholder="Chọn tài xế đang hoạt động"
                loading={loadingDrivers}
                value={assigningDriverId}
                onChange={setAssigningDriverId}
                optionFilterProp="label"
                options={driverOptions.map((driver) => ({
                  value: driver.id,
                  label: driver.phoneNumber
                    ? `${driver.fullName} · ${driver.phoneNumber}`
                    : driver.fullName,
                }))}
              />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                Không bỏ được phân công — muốn thay thì chọn tài xế khác.
              </Typography.Text>
            </div>
          </Space>
        )}
      </Modal>
    </div>
  );
};

export default BusManagePage;
