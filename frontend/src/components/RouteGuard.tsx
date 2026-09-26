import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts';
import type { RoleCode } from '../api/adminUserApi';
import ForbiddenPage from './ForbiddenPage';

export interface RouteGuardProps {
  /** Màn hình cần bảo vệ. */
  children: ReactNode;

  /**
   * Vai trò được phép vào màn hình này. Bỏ trống = mọi người dùng đã đăng nhập đều vào được.
   * Mã vai trò lấy từ `RoleCode`, khớp đúng `Role.Code` dưới CSDL và claim role trong JWT.
   */
  allowedRoles?: readonly RoleCode[];

  /** Đường dẫn chuyển tới khi chưa đăng nhập. Mặc định `/login`. */
  redirectTo?: string;
}

// Vai trò đọc từ token là chuỗi thô; so khớp với danh sách vai trò của route.
function isRoleAllowed(role: string | null, allowedRoles: readonly RoleCode[]): boolean {
  return role !== null && allowedRoles.some((allowed) => allowed === role);
}

/**
 * Chặn truy cập màn hình theo trạng thái đăng nhập và vai trò — phía client.
 *
 * ⚠️ Đây là lớp bảo vệ cho TRẢI NGHIỆM, không phải lớp bảo mật. Nó chỉ giấu màn hình khỏi
 * người dùng không đủ quyền; quyền thật vẫn do backend quyết định bằng RBAC và trả 403
 * (mục D — Quy ước API, docs/03-quy-uoc.md). Sửa được phía client thì cũng chỉ thấy màn
 * hình rỗng, không lấy được dữ liệu.
 *
 * Cách dùng trong `App.tsx` (file của Băng — xem hướng dẫn bàn giao, KHÔNG tự sửa):
 *
 *   <Route
 *     path="/stops"
 *     element={
 *       <RouteGuard allowedRoles={['Admin', 'Manager']}>
 *         <StopManagePage />
 *       </RouteGuard>
 *     }
 *   />
 */
export default function RouteGuard({
  children,
  allowedRoles,
  redirectTo = '/login',
}: RouteGuardProps) {
  const { user, isAuthenticated } = useAuth();
  const location = useLocation();

  // Chưa đăng nhập → đá về màn hình đăng nhập. Ghi lại đường đang vào để sau khi đăng nhập
  // quay lại đúng chỗ, thay vì thả người dùng về trang chủ.
  //
  // Lưu ý: với App.tsx hiện tại, nhánh này chưa bao giờ chạy vì App đã chặn ở tầng ngoài
  // (`!user` → hiện thẳng <AuthPage />, không render <Routes>). Giữ lại để khi tách `/login`
  // thành route riêng, hoặc khi có màn hình công khai (tra cứu tuyến — US 1), guard vẫn đúng.
  if (!isAuthenticated || !user) {
    return <Navigate to={redirectTo} replace state={{ from: location.pathname }} />;
  }

  // Đã đăng nhập nhưng sai vai trò → 403. Không đá về đăng nhập: xem ForbiddenPage.
  if (allowedRoles && allowedRoles.length > 0 && !isRoleAllowed(user.role, allowedRoles)) {
    return <ForbiddenPage />;
  }

  return <>{children}</>;
}
