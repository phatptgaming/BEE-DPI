# Changelog

## 1.2.1-rc — 2026-10-30

### Giao diện

- Sửa nút **ÁP DỤNG DNS**: nền `#FFC928`, chữ `#0B0C10`, SemiBold, các trạng thái
  normal/hover/pressed/disabled đã chính xác.
- Sửa dropdown nhà cung cấp DNS và chế độ DNS: hiển thị tên thân thiện (Cloudflare,
  Google, Quad9, AdGuard, OpenDNS, CleanBrowsing, Control D, Tùy chỉnh) thay vì tên
  loại nội bộ.
- Xóa dòng mô tả giải thích dưới dropdown chế độ DNS.
- Sửa viền ngoài cửa sổ chính: không còn viền trắng hoặc khung native Windows sáng.
- Thống nhất logo ong trong lục giác dùng cho cửa sổ, biểu tượng EXE, khay hệ thống,
  shortcut Start Menu và thanh tác vụ.

### Thương hiệu

- Giữ `BEE DPI` / `Developed by BirenDev`; bỏ `BEE STUDIO • A BirenDev Project`.
- Liên kết xã hội: Facebook, TikTok và Discord (nhãn hiển thị `discord.com/vthienbhat`).

### Phiên bản

- Cập nhật mã nguồn và siêu dữ liệu tệp thực thi lên `1.2.1` (file version `1.2.1.0`).
- Gói phát hành mới: `dist\BEE DPI 1.2.1-rc`.
- Các gói cũ `dist\BEE DPI`, `dist\BEE DPI 1.1.0-rc`, `dist\BEE DPI 1.2.0-rc` giữ
  nguyên, không bị ghi đè.

### Engine

- Không thay đổi: GoodbyeDPI 0.2.3rc3, WinDivert 2.2, đối số khởi chạy, hành vi chuyển
  hướng DNS, giám sát tiến trình, đơn nhất ứng dụng.

---
## 1.2.0-rc — 2026-10-30

Bản phát hành này thiết kế lại toàn bộ giao diện và tách DNS thành cửa sổ riêng.
Engine mạng, tham số khởi chạy và logic xác minh kết nối không thay đổi so với
1.1.0-rc.

### Giao diện chính

- Cửa sổ gọn 424 × 560 DIP, bo góc 22 px, nền tối `#090A0D`, viền và điểm nhấn
  vàng ong `#FFC928` / `#FFD951`, chi tiết lam `#48DDF4`.
- Chỉ còn logo tổ ong, tên **BEE DPI**, dòng **Developed by BirenDev**, một thẻ
  trạng thái, một nút chính và một nút **QUẢN LÝ DNS**.
- Trạng thái rút gọn còn `CHƯA KẾT NỐI` và `ĐÃ KẾT NỐI`; toàn bộ kết quả HTTP
  của Steam, địa chỉ DNS, chẩn đoán IPv4/IPv6 và tham số engine chỉ ghi vào log.
- Thanh tiêu đề tùy biến với nút thu nhỏ và đóng; kéo cửa sổ, đổi kích thước và
  bo góc hoạt động ở 100/125/150%.
- Cửa sổ tự thu nhỏ theo vùng làm việc ở màn hình nhỏ hoặc tỷ lệ cao; không có
  thanh cuộn ở cửa sổ chính.
- Font **Be Vietnam Pro** được đóng gói kèm giấy phép OFL, đủ dấu tiếng Việt.

### Khay hệ thống

- Nút đóng (X) ẩn cửa sổ vào khay, giữ nguyên engine và trạng thái kết nối.
- Nhấp biểu tượng khay để mở lại; menu chuột phải gồm **Mở BEE DPI**,
  **Kết nối / Hủy kết nối** và **Thoát BEE DPI**.
- **Thoát BEE DPI** dừng engine do ứng dụng sở hữu, chờ tiến trình kết thúc, giải
  phóng handle, dừng EngineHost rồi mới gỡ biểu tượng khay và thoát.
- Thông báo `BEE DPI vẫn đang chạy trong khay hệ thống.` chỉ hiện một lần trong
  mỗi phiên, khi cửa sổ thực sự được ẩn.
- Không đăng ký khởi động cùng Windows.

### Quản lý DNS

- DNS chuyển sang cửa sổ riêng, cùng ngôn ngữ thiết kế, chỉ gồm: mạng đang sử
  dụng, nhà cung cấp DNS, chế độ DNS, **Áp dụng DNS** và **Khôi phục DNS ban đầu**.
- Tên nhà cung cấp thân thiện: Cloudflare, Google, Quad9, AdGuard, OpenDNS,
  CleanBrowsing, Control D và Tùy chỉnh.
- DNS tùy chỉnh nằm trong mục **Tùy chọn nâng cao** thu gọn; nhập DNS tùy chỉnh
  sẽ tự mở mục này.
- Tự phát hiện adapter vật lý đang giữ default route theo route metric + interface
  metric, ưu tiên IPv4/IPv6; bỏ qua VPN, tunnel, loopback, Hyper-V, VMware,
  VirtualBox, WSL và adapter ảo/đang tắt.
- Nếu nhiều mạng ngang nhau, ứng dụng yêu cầu chọn; nếu VPN giữ default route,
  ứng dụng cảnh báo thay vì đoán.
- Kiểm tra lại adapter ngay trước khi áp dụng; không dùng lại chỉ số adapter cũ
  khi chuyển Wi-Fi ↔ Ethernet.
- Bản sao lưu gốc theo từng adapter được giữ nguyên, không bị ghi đè khi đổi hồ
  sơ; khôi phục đúng trạng thái DHCP/static và hoàn tác thay đổi DoH do BEE DPI
  tạo.
- Chỉ thay DNS; không đổi IP, gateway, subnet, proxy, route, hosts hay VPN.
- Thanh cuộn tối, viền vàng ong chỉ xuất hiện trong cửa sổ DNS.

### Thương hiệu và đóng gói

- Tên sản phẩm duy nhất là **BEE DPI**; bỏ footer `BEE STUDIO • A BirenDev
  Project` và các khẩu hiệu phụ.
- Một logo ong trong lục giác dùng chung cho giao diện, biểu tượng EXE, khay hệ
  thống, shortcut Start Menu và thanh tác vụ; ICO có đủ 16/24/32/48/64/128/256 px.
- Thêm bộ cài `install.ps1` / `Cai dat BEE DPI.cmd` (tự yêu cầu UAC), tạo shortcut
  Start Menu, đăng ký mục gỡ cài đặt chuẩn và `uninstall.ps1`.
- Liên kết xã hội: Facebook, TikTok và Discord (nhãn hiển thị
  `discord.com/vthienbhat`), đặt kín đáo ở cuối cửa sổ chính.
- Phiên bản 1.2.0; gói phát hành `dist\BEE DPI 1.2.0-rc`.

### Kiểm thử

- Bổ sung kiểm thử cho chọn adapter (chỉ Wi-Fi, chỉ Ethernet, cả hai, ưu tiên
  route, VPN, adapter ảo, mất kết nối, chuyển mạng, khác biệt IPv4/IPv6), adapter
  cũ, DNS an toàn/khôi phục, giao diện, khay, font tiếng Việt và đường dẫn UAC.
- Tổng cộng 85 kiểm thử tự động, không thay đổi cấu hình mạng thật.

### Giới hạn đã biết

- Chưa kiểm thử thay đổi DNS thật và kết nối engine thật trong phiên bản này vì
  cần sự đồng ý rõ ràng của người dùng.
- Chưa có bộ cài `.exe` đóng gói sẵn; bộ cài hiện là script PowerShell có UAC,
  kèm shortcut và mục gỡ cài đặt.
- Hành vi DPI phụ thuộc ISP, khu vực và thời điểm; không có tuyên bố vượt DPI
  phổ quát.
