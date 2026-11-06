# Dynamic Island for Windows

A sleek, iOS-inspired Dynamic Island notification overlay for Windows 11. Replaces the default Windows toast notifications with a beautiful, animated pill that drops down from the top of your screen.

![Windows 11](https://img.shields.io/badge/Windows-11-0078D6?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green)

![DIN](https://github.com/user-attachments/assets/59539079-a559-4a2a-a816-9133f4aefe4c)

## 🇻🇳 Bản chỉnh sửa (fork)

Đây là bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (giấy phép MIT), làm để chia sẻ với bạn bè. Danh sách thay đổi từng bản nằm trong [RELEASE_NOTES.md](RELEASE_NOTES.md).

### Có gì

- **Đảo thông báo**: thông báo từ app và trang web (Messenger, Gmail, Zalo…) hiện trên một viên ở mép trên màn hình, đúng tên và logo của trang; di chuột để xem đủ nội dung, bấm để mở đúng trang; tắt tiếng theo từng app
- **Tai thỏ**: viên nhỏ luôn ở giữa mép trên với đồng hồ LED và sóng nhạc theo âm thanh thật; di chuột vào để mở bảng:
  - **Nhạc**: ảnh bìa, lời bài hát chạy theo nhạc (LRCLIB, NetEase), thanh tua, âm lượng từng app, nền **nốt nhạc trôi** theo màu bài hát
  - **Trồng cây** tập trung 5 phút – 3 giờ, 12 loại cây, Deep focus nhắc khi mở YouTube / Facebook…, thống kê ngày / tuần / tháng / năm
  - **Thông báo** gần đây, **Copy** (lịch sử văn bản và hình ảnh), **Hạn chót** (gõ "Nộp báo cáo 15/11 17h", nhắc trước 1 ngày / 1 giờ)
- Mắt và chú mèo trên viên, chế độ thuyết trình (tự ẩn khi trình chiếu / chia sẻ màn hình), báo pin, tai nghe Bluetooth, tự cập nhật

### Cài đặt

**Cách 1: bộ cài.** Vào **Releases**, tải `DynamicIslandSetup-x64.exe` (máy chip Snapdragon: `-arm64`) và chạy. Không cần quyền quản trị. Gỡ ở **Settings → Apps**.

**Cách 2: lệnh PowerShell** (cài vào cùng chỗ với bộ cài):

```powershell
Stop-Process -Name DynamicIslandNotifier -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$dir = "$env:LOCALAPPDATA\Programs\DynamicIsland"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Invoke-WebRequest -Uri "https://github.com/khaibadao/dynamic-island-on-windows/releases/latest/download/DynamicIslandNotifier-x64.exe" -OutFile "$dir\DynamicIslandNotifier.exe"
Unblock-File -Path "$dir\DynamicIslandNotifier.exe"
Start-Process -FilePath "$dir\DynamicIslandNotifier.exe"
```

Sau đó:
1. Windows báo *"Windows protected your PC"* → **More info** → **Run anyway** (file chưa được ký số)
2. Cho phép đọc thông báo: **Settings → Privacy & Security → Notifications**
3. Để thông báo web lên đảo: **Settings → System → Notifications → Microsoft Edge** → bật **Show notifications in notification center**

Yêu cầu: Windows 11 (build 22621+). Không cần cài .NET.

### Quyền riêng tư

Mọi thứ được xử lý trên máy bạn. Menu khay → **Cài đặt & quyền riêng tư…** có giải thích và công tắc cho từng mục.

| Cái gì | Ở đâu | Gửi đi đâu |
|---|---|---|
| Nội dung thông báo, lịch sử copy | Chỉ trên máy, **mã hoá** (Windows DPAPI, chỉ tài khoản của bạn đọc được) | Không gửi |
| Tên cửa sổ đang mở (Deep focus, thuyết trình) | Chỉ xem, không lưu | Không gửi · có công tắc |
| Âm thanh loa (sóng nhạc) | Chỉ đo độ to theo dải, không ghi âm | Không gửi · có công tắc |
| Tên miền trang web gửi thông báo | Logo lưu trên máy | Dịch vụ biểu tượng của Google · có công tắc |
| Tên bài hát, ca sĩ | | LRCLIB; NetEase (có công tắc) |
| Kiểm tra bản mới | | GitHub, khi mở app và mỗi 6 giờ |

Khi app chạy, banner của Windows được ẩn cho những app đã hiện trên đảo (để không hiện hai lần). Thoát app, đăng xuất, tắt mục này trong Cài đặt, hoặc gỡ app là **trả lại như cũ**.

### Cập nhật

App tự báo khi có bản mới; bấm vào sẽ hiện danh sách thay đổi rồi mới cài. File tải về được kiểm tra mã **SHA-256** (`SHA256SUMS.txt` kèm theo mỗi bản phát hành) trước khi thay.

### Báo lỗi

Mở một mục ở tab **Issues**. Kèm theo file `%LOCALAPPDATA%\DynamicIslandNotifier\startup.log` nếu được (file này không chứa nội dung tin nhắn).

### Cho người muốn build

```bash
dotnet test tests/DynamicIslandNotifier.Tests   # kiểm thử (phần đọc ngày hạn chót, phần cập nhật)
dotnet publish DynamicIslandNotifier.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

GitHub Actions build bản x64 và arm64, bộ cài Inno Setup và `SHA256SUMS.txt`, rồi phát hành khi số trong file `VERSION` đổi.

---

## Features

- **Dynamic Island Pill** — Animated notification overlay at the top-center of your screen
- **iOS-style Animations** — Drop-down bounce, horizontal expand, slide-up exit
- **Interactive Hover Actions** — Hover to reveal Mute and Clear buttons
- **Native Toast Suppression** — Automatically hides Windows default notifications
- **Click to Open** — Click the notification to launch the source app
- **Deterministic Accent Colors** — Each app gets a unique, consistent avatar color
- **Always on Top** — Works over fullscreen apps and games
- **System Tray** — Runs silently in the background with tray icon controls

## Prerequisites

- **Windows 11** (build 22621 or later)
- [**.NET 8 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Notification Access** — The app will request permission on first launch

## Getting Started

```bash
# Clone the repository
git clone https://github.com/sadeeshasathsara/dynamic-island-windows.git
cd dynamic-island-windows

# Build
dotnet build

# Run
dotnet run
```

On first launch, Windows will ask you to grant notification listener access. Go to:
**Settings → Privacy & Security → Notifications** → enable **Dynamic Island Notifier**.

> **Tip:** Enable **Do Not Disturb** in Windows to fully suppress native toast banners while the Dynamic Island handles everything.

## Architecture

```
├── Core/
│   ├── NotificationData.cs      # Immutable data model
│   └── NativeInterop.cs         # Win32 P/Invoke utilities
├── Services/
│   ├── NotificationListener.cs  # WinRT notification polling + events
│   └── ToastSuppressor.cs       # Registry-based DND management
├── UI/
│   └── IslandAnimator.cs        # Animation primitives & orchestration
├── IslandWindow.xaml / .cs      # The pill overlay window
├── App.xaml / .cs               # Entry point & system tray
└── Assets/
    └── island.ico               # App icon
```

| Layer | Responsibility |
|-------|---------------|
| **Core** | Data models, Win32 interop — zero UI dependencies |
| **Services** | Notification listening, toast suppression — zero UI dependencies |
| **UI** | Animation engine, overlay window — depends on Core only |

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License — see [LICENSE](LICENSE) for details.
