# Hệ thống thư viện và đặt mượn sách phân tán

Ứng dụng dùng .NET 10 Minimal API, PostgreSQL, RabbitMQ và một Reservation Worker độc lập. Giao diện HTML/CSS/JavaScript được API phục vụ tại cùng origin.

## Kiến trúc

```text
Browser -> Library API -> PostgreSQL (Reservation + Outbox)
                         Outbox Publisher -> RabbitMQ -> Reservation Worker -> PostgreSQL
```

API ghi yêu cầu `Pending` cùng sự kiện Outbox trong một lần lưu database. Publisher gửi sự kiện bền vững đến RabbitMQ; worker xác nhận số bản còn trong transaction và cập nhật `Reserved` hoặc `Rejected`. Xử lý khóa hàng và chỉ xử lý yêu cầu đang `Pending` giúp tránh trừ tồn kho hai lần khi message được giao lại.

## Chạy toàn bộ bằng Docker Compose

1. Cài và khởi động Docker Desktop.
2. Tùy chọn: sao chép `.env.example` thành `.env`, sau đó đổi mật khẩu local.
3. Tại thư mục repository chạy:

```powershell
docker compose up --build
```

Mở giao diện tại <http://localhost:5173>, RabbitMQ Management tại <http://localhost:15672> (dùng thông tin từ `.env`). PostgreSQL chỉ được expose trong mạng Compose. Dữ liệu database được giữ trong volume `postgres_data`. Compose đặt API ở Development để có thể dùng các tài khoản mẫu và kiểm tra link reset.

## Chạy bằng Visual Studio

API và worker chạy bằng Visual Studio; PostgreSQL và RabbitMQ chạy trong Docker:

```powershell
docker compose up -d postgres rabbitmq
```

Mở `LibrarySystem.sln`, khôi phục NuGet. Đặt `PingApi` làm Startup Project và chạy F5. Sau đó mở project `Reservation.Worker` trong một cửa sổ Visual Studio thứ hai và chạy. Connection mặc định cho chế độ chạy local là `Host=localhost;Port=5432;Database=library;Username=library;Password=library_dev`; RabbitMQ dùng `localhost:5672`, user `library`, password `library_dev`. Có thể ghi đè bằng User Secrets hoặc biến môi trường `ConnectionStrings__LibraryDb`, `RabbitMq__HostName`, `RabbitMq__Port`, `RabbitMq__UserName`, `RabbitMq__Password`.

## API

- `GET /books?q=&category=` — danh sách/tìm kiếm/lọc sách.
- `GET /books/{id}` — chi tiết sách.
- `GET /users` — danh sách bạn đọc mẫu.
- `POST /reservations` — tạo yêu cầu bất đồng bộ, trả HTTP 202.
- `GET /reservations/{id}` — xem trạng thái yêu cầu.
- `GET /users/{userId}/reservations` — yêu cầu của bạn đọc.
- `POST /reservations/{id}/borrow` — chuyển `Reserved` thành `Borrowed`.
- `POST /reservations/{id}/return` — chuyển `Borrowed` thành `Returned` và cộng lại tồn kho.
- `GET /health`, `GET /health/ready` — kiểm tra tiến trình và database.
- `POST /auth/login`, `GET /auth/me`, `POST /auth/logout` — phiên đăng nhập bằng cookie HttpOnly.
- `POST /auth/forgot-password`, `POST /auth/reset-password` — gửi link email và đặt mật khẩu mới bằng token một lần, hết hạn sau 30 phút.

Ví dụ tạo yêu cầu:

```json
{ "userId": "10000000-0000-0000-0000-000000000001", "bookId": "20000000-0000-0000-0000-000000000001" }
```

Trong môi trường Development, đăng nhập bằng `minhanh@example.com` hoặc `0901234567`, mật khẩu `MocLibrary@2026`. Yêu cầu đặt lại mật khẩu trả một link dùng một lần trên màn hình phát triển để có thể thử ngay. Để gửi email thật, cấu hình `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_ENABLE_SSL` trong `.env`; nên đặt `ASPNETCORE_ENVIRONMENT=Production` khi không còn dùng tài khoản mẫu và đặt `APP_PUBLIC_BASE_URL` theo địa chỉ HTTPS công khai của API.

Các bảng được tạo bằng `EnsureCreated` cho bản demo; khi khởi động, API thêm các cột tài khoản cần thiết cho database đã tồn tại. Khi phát triển tiếp để triển khai thật, hãy chuyển sang EF Core migrations.

## Ghi chú giao diện

Trang danh mục kết nối API và tự cập nhật trạng thái yêu cầu. Sidebar có khu vực tổng quan, sách yêu thích, bộ lọc trạng thái yêu cầu và hồ sơ bạn đọc. Khi yêu cầu ở `Reserved`, bạn đọc có thể xác nhận đã nhận sách; khi ở `Borrowed`, có thể xác nhận đã trả. API giới hạn lịch sử theo user trong cookie phiên. Cấu hình SMTP là cần thiết để nhận link đặt lại trong email thật.
