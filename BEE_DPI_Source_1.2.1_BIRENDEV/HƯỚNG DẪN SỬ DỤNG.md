BEE DPI
=======

BEE DPI là ứng dụng Windows gọn nhẹ giúp bật/tắt nhanh lớp chống can thiệp DPI cho kết nối Internet.

Developed by BirenDev

BEE DPI là sản phẩm độc lập của BirenDev.

Phiên bản: 1.2.1-rc3
Thư mục phát hành: dist\BEE DPI 1.2.1-rc3


KHAY HỆ THỐNG
--------------

- Nhấp trái biểu tượng khay để mở lại cửa sổ BEE DPI.
- Nhấp phải để mở menu:
  - Mở BEE DPI
  - Kết nối hoặc Hủy kết nối (theo trạng thái hiện tại)
  - Thoát BEE DPI
- Khi cửa sổ được ẩn lần đầu trong phiên, Windows có thể hiển thị thông báo:
  "BEE DPI vẫn đang chạy trong khay hệ thống."
  (Tùy thiết lập thông báo của Windows.)
- Khi chọn Thoát BEE DPI, ứng dụng dừng engine do chính ứng dụng sở hữu, chờ tiến trình kết thúc, giải phóng handle, dừng EngineHost, gỡ biểu tượng khay rồi mới thoát.


QUYỀN RIÊNG TƯ VÀ HÀNH VI MẠNG
------------------------------

BEE DPI không thu thập dữ liệu, không gửi thống kê và không kết nối tới máy chủ nào của BirenDev. Ứng dụng chỉ:

- Chạy engine đã được đóng gói sẵn trong thư mục Engine.
- Gửi HTTPS GET ẩn danh tới ba trang Steam công khai để kiểm tra kết nối khi engine đang chạy. Ứng dụng không đăng nhập, không gửi cookie hoặc thông tin tài khoản.
- Mở một named pipe cục bộ, có tên ngẫu nhiên và chỉ dành cho phiên hiện tại, để điều khiển tiến trình engine đã được nâng quyền.
- Chạy script DNS cố định tools\dns-manager.ps1 khi bạn sử dụng cửa sổ DNS.
- Ghi log chẩn đoán cục bộ vào %LOCALAPPDATA%\BeeDPI\logs.

Engine bên thứ ba có thể thay đổi cách Windows xử lý một số gói tin mạng để xử lý hiện tượng DPI. Với cấu hình 1.2.1-rc3 (giữ nguyên cấu hình mạng của 1.2.1-rc), engine tự chuyển tiếp gói DNS UDP tới 1.1.1.1:53 và [2606:4700:4700::1111]:53.

BEE DPI không sửa DNS của adapter, không cài proxy và không sửa registry mạng, trừ khi bạn chủ động áp dụng DNS trong cửa sổ Quản lý DNS. Khi sử dụng DNS nâng cao, truy vấn được gửi tới nhà cung cấp mà bạn chọn.


HỖ TRỢ
--------

Facebook: https://www.facebook.com/vthienbhat/
TikTok: https://www.tiktok.com/@nthienbhat
Discord: https://discord.com/users/977745837176266813


GIẤY PHÉP
---------

Mã nguồn giao diện và các thành phần do BirenDev phát triển: © 2026 BirenDev.
Các thành phần bên thứ ba giữ nguyên giấy phép và bản quyền tương ứng như được nêu trong thư mục LICENSE và tài liệu THIRD-PARTY-NOTICES.