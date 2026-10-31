Bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (MIT).

### Sửa lỗi trong v1.2.1
- Sửa lỗi **thông báo lúc hiện lúc không**: khi thông báo mới đến đúng lúc đảo đang thu lại, hiệu ứng thu lại cũ kéo luôn thông báo mới đi, và đảo có thể bị kẹt không hiện nữa cho tới khi khởi động lại app
- Tin nhắn mới trong cùng một cuộc trò chuyện (Messenger…) không còn bị bỏ qua
- Một thông báo lỗi không còn làm mất các thông báo đến cùng lúc
- Không còn tắt toàn bộ thông báo Windows khi app chạy (trước đây nếu app bị tắt ngang, thông báo Windows có thể bị tắt luôn)

### Có từ v1.2.0
- **Nút Mute hoạt động thật**: tắt thông báo theo từng app / từng trang web. Bật lại ở menu khay hệ thống → **Muted**
- **Đảo báo pin**: hiện khi cắm / rút sạc, cảnh báo khi pin còn 20% và 10% (bấm vào để mở cài đặt pin)
- **Lịch sử thông báo**: menu khay hệ thống → **Recent notifications** (15 thông báo gần nhất, bấm để mở lại)
- File tải về nhẹ hơn nhiều (đã nén)

### Đã có từ v1.1.0
- Thông báo web (Messenger, Claude, Gmail, Zalo…) hiện đúng tên + logo của trang
- Di chuột vào đảo để xem toàn bộ nội dung; bấm vào để mở đúng tab đã gửi thông báo
- Trình phát nhạc (Spotify, YouTube…): viên nhỏ có sóng nhạc, kéo đặt ở đâu cũng được; di chuột vào để có thanh tua, ⏮ ⏯ ⏭ và âm lượng riêng cho app đang phát
- Tự chạy khi bật máy (tắt được ở menu khay hệ thống)

### Tải về
- **DynamicIslandNotifier-x64.exe**: hầu hết máy tính (Intel / AMD)
- **DynamicIslandNotifier-arm64.exe**: máy chip Snapdragon

### Cài đặt
1. Tải file `.exe` phù hợp và chạy (không cần cài .NET)
2. Nếu Windows báo *"Windows protected your PC"* → **More info** → **Run anyway**
3. Cho phép đọc thông báo: **Settings → Privacy & Security → Notifications**
4. Để thông báo web lên đảo: **Settings → System → Notifications → Microsoft Edge** → tick **Show notifications in notification center**

Yêu cầu Windows 11 (build 22621 trở lên).
