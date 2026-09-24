namespace SmartBus.Api.Services;

/// <summary>
/// Loại lỗi nghiệp vụ — Controller đổi thành mã HTTP tương ứng (docs/03-quy-uoc.md mục D2).
/// Service không biết gì về HTTP; nó chỉ nói lỗi này thuộc loại nào, để Controller không phải
/// đoán ý nghĩa câu thông báo.
/// </summary>
public enum ServiceErrorKind
{
    /// <summary>Dữ liệu đầu vào không hợp lệ → 400.</summary>
    Invalid,

    /// <summary>Không tìm thấy dữ liệu → 404.</summary>
    NotFound,

    /// <summary>Xung đột với trạng thái hiện tại (trùng SĐT, tự khóa chính mình…) → 409.</summary>
    Conflict,
}

/// <summary>
/// Kết quả nghiệp vụ trả từ Service lên Controller — Controller chỉ đổi thành HTTP status,
/// không tự phán đoán nghiệp vụ. Cùng khuôn với <see cref="AuthResult"/> nhưng dùng chung được
/// cho mọi kiểu dữ liệu, nên không phải nhân bản lớp kết quả cho từng nghiệp vụ.
/// </summary>
public class ServiceResult<T>
{
    private ServiceResult(
        bool success,
        T? data,
        string? error,
        ServiceErrorKind errorKind,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        Success = success;
        Data = data;
        Error = error;
        ErrorKind = errorKind;
        Errors = errors;
    }

    public bool Success { get; }

    public T? Data { get; }

    /// <summary>Thông báo lỗi hiển thị cho người dùng cuối — không chứa chi tiết kỹ thuật.</summary>
    public string? Error { get; }

    /// <summary>Chỉ có nghĩa khi <see cref="Success"/> là false.</summary>
    public ServiceErrorKind ErrorKind { get; }

    /// <summary>
    /// Lỗi theo từng trường để frontend gắn trực tiếp vào ô input (ví dụ SĐT đã tồn tại).
    /// Cùng cấu trúc { message, errors } thống nhất của dự án — xem docs/01-kien-truc.md.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static ServiceResult<T> Ok(T data)
        => new(true, data, null, ServiceErrorKind.Invalid, null);

    public static ServiceResult<T> Invalid(string error, IReadOnlyDictionary<string, string[]> errors)
        => new(false, default, error, ServiceErrorKind.Invalid, errors);

    public static ServiceResult<T> NotFound(string error)
        => new(false, default, error, ServiceErrorKind.NotFound, null);

    public static ServiceResult<T> Conflict(string error)
        => new(false, default, error, ServiceErrorKind.Conflict, null);
}
