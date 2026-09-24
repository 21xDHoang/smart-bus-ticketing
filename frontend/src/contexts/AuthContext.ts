import { createContext, useContext } from 'react';
import type { DecodedUser, LoginPayload } from '../api/authApi';

// Trạng thái đăng nhập dùng chung cho toàn app.
// Tách phần "định nghĩa context + hook" (file .ts, không có JSX) khỏi phần
// "component Provider" (AuthProvider.tsx) để không vi phạm quy tắc lint
// react/only-export-components — một file không nên vừa export component vừa export hook.
export interface AuthContextValue {
  /** Người dùng hiện tại, null nếu chưa đăng nhập. */
  user: DecodedUser | null;

  isAuthenticated: boolean;

  /** Gọi API đăng nhập, lưu token và cập nhật trạng thái. Ném AppError nếu thất bại. */
  login: (payload: LoginPayload) => Promise<DecodedUser>;

  /** Thu hồi refresh token phía server rồi xoá phiên phía client. */
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Truy cập trạng thái đăng nhập ở bất kỳ component nào nằm trong <AuthProvider>.
 * Route guard (task của Thịnh) và mọi màn hình cần biết "ai đang đăng nhập" đều dùng hook này.
 */
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error('useAuth phải được dùng bên trong <AuthProvider>.');
  }

  return context;
}
