# Dynamic Island for Windows

A sleek, iOS-inspired Dynamic Island notification overlay for Windows 11. Replaces the default Windows toast notifications with a beautiful, animated pill that drops down from the top of your screen.

![Windows 11](https://img.shields.io/badge/Windows-11-0078D6?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green)

![DIN](https://github.com/user-attachments/assets/59539079-a559-4a2a-a816-9133f4aefe4c)

## 🇻🇳 Bản chỉnh sửa (fork)

Đây là bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (giấy phép MIT). Những gì đã thêm:

- **Thông báo từ trang web** (Messenger, Claude, Gmail, Zalo…): hiện đúng tên và logo của trang thay vì logo trình duyệt
- **Di chuột vào đảo** để xem toàn bộ nội dung thông báo
- **Tai thỏ** ở giữa mép trên màn hình: di chuột vào để mở trình phát nhạc (đổi màu theo ảnh bìa, thanh tua, ⏮ ⏯ ⏭, âm lượng riêng từng app), **trồng cây tập trung** kiểu Forest, giờ & pin, thông báo gần đây; hiện thanh âm lượng khi chỉnh âm lượng
- **Bấm vào đảo** để mở đúng tab/trang đã gửi thông báo
- **Nút Mute** tắt thông báo theo từng app / trang (bật lại ở menu khay → *Muted*), **lịch sử thông báo** (*Recent notifications*)
- **Đảo báo pin**: cắm / rút sạc, cảnh báo pin yếu 20% và 10%
- **Tự chạy khi bật máy** (bật/tắt ở menu khay hệ thống: *Start with Windows*)
- Sửa lỗi đảo không tự thu lại khi có thông báo mới đến lúc đang mở

### Cài đặt nhanh

1. Vào trang **Releases** bên phải, tải `DynamicIslandNotifier-x64.exe` (máy chip Snapdragon: `-arm64.exe`) và chạy (không cần cài .NET)
2. Windows báo *"Windows protected your PC"* → **More info** → **Run anyway** (file chưa được ký số)
3. Cho phép đọc thông báo: **Settings → Privacy & Security → Notifications**
4. Để thông báo web lên đảo: **Settings → System → Notifications → Microsoft Edge** → tick **Show notifications in notification center**

Yêu cầu: Windows 11 (build 22621+). App đọc thông báo ngay trên máy, không gửi nội dung đi đâu (chỉ gửi tên miền tới dịch vụ favicon của Google để tải logo). Thoát app bằng **Quit** ở khay hệ thống để bật lại thông báo kiểu cũ của Windows.

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
