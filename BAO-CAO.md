# BÁO CÁO DỰ ÁN — OneWorshipCenter / Worship Backtrack Player

*Người viết: Kiro (thực thi). Task: T1 — spec đợt 1 "Khóa bằng chứng & nối mắt xích cuối của Slides".*
*Nhánh: `kiro-current`. Commit HEAD khi viết báo cáo: `b245e07` ("fix: reliable touch + mouse input across all three Slides editor modals").*

---

## 1. Phần mềm là gì, công nghệ, tổ chức solution

### 1.1. Phần mềm là gì
OneWorshipCenter (tên solution: **WorshipBacktrackPlayer**) là phần mềm chạy nhạc nền đa track (multitrack backtrack) chuyên nghiệp cho hội thánh/ban thờ phượng, chạy trên **Windows**. Ngoài phần phát nhạc, phần mềm còn có khối trình chiếu (Presenter) để hiển thị lời bài hát (lyric), slide tự tạo, và câu Kinh Thánh ra màn hình khán giả.

Các nhóm chức năng chính (theo README):
- **Audio engine**: WASAPI exclusive mode (độ trễ ~3-5ms), 8+ track đồng thời (volume/solo/mute riêng), đổi tempo thời gian thực (50%-150%) qua SoundTouch, dịch cao độ (pitch) tới ±6 nửa cung, chia kênh trái/phải (FOH / IEM-Click).
- **Quản lý section**: điểm loop chính xác mili-giây, loop liền mạch, nhảy section, sắp xếp bố cục (arrangement), nhập theo ms / timecode / số ô nhịp.
- **Quản lý bài hát & setlist**: nhiều bài, cấu hình bằng JSON, tự dò stem trong thư mục, tự gắn nhãn track, setlist có transition.
- **Điều khiển ánh sáng (MaestroDMX)**: OSC qua UDP (chính), MIDI (dự phòng).
- **Presenter / Slides / Kinh Thánh**: cửa sổ Operator + Output, stage display qua web server (Kestrel), video nền (LibVLC), NDI (hiện là stub).

### 1.2. Công nghệ (Technology Stack)

| Thành phần | Công nghệ |
|---|---|
| Nền tảng | .NET 8, Windows 10/11 x64 |
| UI | WPF |
| Audio | NAudio + WASAPI |
| Tempo/Pitch DSP | SoundTouch.Net |
| Ánh sáng (chính) | OSC qua UDP |
| Ánh sáng (dự phòng) | MIDI (NAudio.Midi) |
| Lưu trữ | JSON (System.Text.Json) là nguồn sự thật, SQLite (Microsoft.Data.Sqlite) là bản gương |
| Stage display | ASP.NET Core Kestrel (web server nội bộ) |
| Video nền | LibVLC |
| NDI output | Hiện là **stub** (chưa nối SDK NDI thật) |
| Test | xUnit |

### 1.3. Tổ chức solution

`WorshipBacktrackPlayer.sln` gồm **5 project** (.NET 8):

| Project | Target | Vai trò |
|---|---|---|
| **WorshipPlayer.Core** | `net8.0` (thuần, không UI) | Models + ~40 service nghiệp vụ (ServiceOrderManager, SlidePresentationService, SlideGroupingService, SlideAutoTimingService, SlideLinkReconciler, SlidePresentationMapper, ChordProParserService, ScriptureSlideGenerator, BibleDatabaseService, MediaLibraryService...). JSON là nguồn sự thật, SQLite là bản gương. |
| **WorshipPlayer.Audio** | `net8.0-windows` | Engine backtrack NAudio/SoundTouch (WASAPI, tempo/pitch, đồng bộ click). |
| **WorshipPlayer.Presenter** | `net8.0-windows` (WPF) | Cửa sổ Operator/Output, stage display qua Kestrel, LibVLC cho video, NDI (stub), các cửa sổ/panel Slides + Scripture + Settings. |
| **WorshipPlayer.UI** | `WinExe` (`WorshipBacktrackPlayer`), `net8.0-windows` | Điểm vào: `App.xaml.cs` → `MainWindow` (backtrack player) → `PresenterIntegration` mở `OperatorWindow`. |
| **WorshipPlayer.Tests** | `net8.0`, xUnit | Project test. Tại HEAD này **chỉ tham chiếu `WorshipPlayer.Core`** (`ProjectReference` duy nhất), test phần Core và một phần service được link source. 71 file test, 764 thuộc tính `[Fact]`/`[Theory]`. |

Thống kê repo tại HEAD: 245 file `.cs`, 58 file `.xaml`, 71 file test. 25 file `.mp3` (~108MB) đang được git track ở thư mục gốc và trong `Songs/` (xem phần 3.4 — cảnh báo không xóa).

---

## 2. Trạng thái THẬT từng tính năng (đã xong / đang dở / chưa bắt đầu)

*Phần này đối chiếu trực tiếp code tại HEAD `b245e07` và báo cáo nghiệm thu của Muse (`.kiro/reviews/nghiem-thu-dot-1.md` trên nhánh main). Mọi điểm đã được kiểm lại bằng grep/đọc code, không chép nguyên văn.*

### 2.1. Lyric Presenter / Slides editor (CHI TIẾT — trọng tâm)

**Kết luận: phần lõi đã làm THẬT và đã nối dây; đạt mức "xong về code". Còn thiếu bằng chứng chạy thật trên máy Windows (chạm/bấm, z-order) và một mắt xích cấu hình chưa nối (xem N-2).**

Đã kiểm chứng trong code:
- **3 cửa sổ modal Slides tồn tại và được dựng từ luồng thật** (file có mặt tại HEAD):
  - `WorshipPlayer.Presenter/Views/Slides/SlideServiceItemEditorWindow.xaml(.cs)` — editor cho Slides service item.
  - `WorshipPlayer.Presenter/Views/Slides/SlideEditorWindow.xaml(.cs)` — editor slide.
  - `WorshipPlayer.Presenter/Views/Slides/SlidesLibraryPanel.xaml(.cs)` — thư viện presentation.
  - Kèm `ListPickerDialog` (chọn presentation + template) và `SlideNameDialog` (đặt tên) trong cùng thư mục.
- **Cơ chế live-link (thay vì snapshot) đã hoàn chỉnh**: `WorshipPlayer.Core/Services/SlidePresentationMapper.cs` (ánh xạ sang link), `WorshipPlayer.Core/Services/SlideLinkReconciler.cs` (dọn liên kết mồ côi khi xóa/load). Hiển thị tên trực tiếp (live name) ở sidebar + header. Cả hai service thuần đều có test: `SlidePresentationMapperTests.cs`, `SlideLinkReconcilerTests.cs`.
- **Auto-timed slides** (spec `.kiro/specs/auto-timed-slides`): hai service thuần `SlideGroupingService` + `SlideAutoTimingService` có mặt và đều có test riêng (`SlideGroupingServiceTests.cs`, `SlideAutoTimingServiceTests.cs`). Pipeline nhận tham số `--lines-per-slide` / `--slide-lead-ms`.
- **Không có nợ kỹ thuật giấu mặt trong phạm vi Slides**: không thấy TODO/FIXME/`NotImplementedException` trong code tính năng này.

**Mắt xích đang dở (đã kiểm lại tại HEAD, trùng điểm N-2 của Muse):**
- `PresenterSettings.LinesPerSlide` / `PresenterSettings.SlideLeadMs` (cấu hình toàn cục trong Settings) **chưa được truyền vào `SectionEditorWindow`**. Tại điểm mở duy nhất `WorshipPlayer.UI/Views/MainWindow.xaml.cs:608` (`EditSections_Click`), constructor `SectionEditorWindow` được gọi **không set** 2 property `LinesPerSlide`/`SlideLeadMs`. Hai property này tồn tại và mang mặc định `4` và `500` (`SectionEditorWindow.xaml.cs:69` và `:75`) và thực sự đi tiếp vào pipeline (`SectionEditorWindow.xaml.cs:1179-1180`), nhưng vì không được nạp từ Settings nên **editor luôn dùng mặc định 4 dòng / 500ms** dù người dùng đổi Settings.
- → Đây là phạm vi của **task T3**, KHÔNG sửa trong phiên T1 này.

**Chưa có bằng chứng chạy thật (trùng N-3):** tiêu chí "chạm/bấm ăn ngay trên 3 modal Slides khi đang Live" của commit `b245e07` chưa được kiểm chứng lúc chạy (môi trường test không có màn hình cảm ứng). → Phạm vi task **T2**, không làm trong T1.

### 2.2. Audio engine (backtrack)
- **Đã xong về code**: engine NAudio/SoundTouch (WASAPI, tempo/pitch, chia kênh). Nằm ở `WorshipPlayer.Audio` (`net8.0-windows`). Chưa kiểm chứng chạy thật trong phiên này (cần máy Windows + thiết bị audio).

### 2.3. Quản lý section / bài hát / setlist
- **Đã xong về code**: JSON là nguồn sự thật, Section Editor, loop chính xác ms, nhập ms/timecode/bar. Service nghiệp vụ nằm ở Core và có test.

### 2.4. Ánh sáng (MaestroDMX)
- **Đã xong về code** phần OSC/MIDI. Lưu ý: `MidiLightingController` tự khai là stub ở một số điểm (theo review N-8). Chưa kiểm chứng với thiết bị thật.

### 2.5. Settings
- **Đang dở / phần lớn là placeholder**: theo review (N-5), 9/10 section Settings là placeholder tĩnh chưa lưu trữ. Nút Import XML Kinh Thánh chỉ hiện MessageBox, chưa có parser. (Trạng thái này cần xác nhận chi tiết ở task riêng; trong T1 chỉ ghi nhận.)

### 2.6. Dữ liệu Kinh Thánh (Scripture)
- **Đang dở**: đã kiểm lại `WorshipPlayer.Core/Services/Bible/BibleSeedData.cs`:
  - Metadata đủ 66 sách (`GetBookData`).
  - **Nội dung câu** chỉ có **4 chương bản KJV**: Genesis 1, Psalms 23, John 3, Romans 8.
  - Bản **WEB** chỉ có bản ghi translation (`IsDefault = 0`), **0 câu**.
- Parser tham chiếu (`ScriptureReferenceParser`), generator slide (`ScriptureSlideGenerator`), `BibleDatabaseService` có mặt và có test. → Khoảng trống lớn nhất là **dữ liệu câu** chưa đầy đủ.

### 2.7. Video nền / NDI
- **Đang dở (có đường ống nhưng "tối")**:
  - NDI: `WorshipPlayer.Presenter/Services/StubNdiOutput.cs` — xác nhận là **stub**: nhận frame, đếm `_frameCount`, log, KHÔNG truyền gì qua mạng. Header file ghi rõ cần NewTek NDI SDK thật để thay thế.
  - Video nền (LibVLC) theo review render vào placeholder `ContentControl` thay vì `VideoView` thật (N-6) — chưa ra hình thật. (Ghi nhận theo review; không sửa trong T1.)

### 2.8. Các panel/dialog "mồ côi" (chưa nối dây)
- Theo review (N-4): `ServiceGroupPanel`, `BulkEditPanel`, `ItemSettingsPanel`, `PrintExportDialog`, `ImportMediaDialog`, `MediaMetadataPanel`, `ScriptureSlidePreview` — có file nhưng có thể chưa được khởi tạo từ luồng thật. → Đây là phạm vi **task T4** (lập bảng trạng thái), không xử lý trong T1.

### 2.9. Placeholder nhỏ rải rác
- Theo review (N-8): `TourRequested` no-op, "Open in Planning" placeholder, Ctrl+K placeholder, checkbox chọn ở `SongLibraryPanel` chưa bind. Ghi nhận, không sửa trong T1.

---

## 3. Cách build và cách chạy app trên Windows (từng bước)

> **Lưu ý môi trường:** app là WPF `net8.0-windows`, **chỉ build và chạy được trên Windows**. Trên Linux (sandbox) build sẽ báo lỗi `NETSDK1100` vì thiếu targeting pack Windows desktop/WPF (xem kết quả lệnh đã ghi trong `PROGRESS.md` nhánh main). Các bước dưới đây dành cho máy **Windows 10/11 x64**.

### 3.1. Môi trường cần có
- Windows 10/11 (64-bit).
- **.NET 8.0 SDK** (tải tại https://dotnet.microsoft.com/download/dotnet/8.0 ).
- (Khuyến nghị) Visual Studio 2022 hoặc VS Code + extension C#.
- Thiết bị audio để chạy thật (WASAPI). Để hiển thị video/NDI cần LibVLC/NDI SDK theo cấu hình.

### 3.2. Build (PowerShell trên Windows)
```powershell
cd <thư-mục-repo>

# Build toàn solution
dotnet build WorshipBacktrackPlayer.sln -c Debug

# Hoặc build riêng project chạy chính
dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug
```

### 3.3. Chạy app
```powershell
# Chạy từ source
dotnet run --project WorshipPlayer.UI
```
Hoặc mở `WorshipBacktrackPlayer.sln` trong Visual Studio, đặt `WorshipPlayer.UI` làm startup project, nhấn **F5**.

Xuất bản bản .exe độc lập:
```powershell
dotnet publish WorshipPlayer.UI -c Release -r win-x64 --self-contained -o ./publish
```

### 3.4. Dữ liệu chạy thật nằm ở đâu — CẢNH BÁO
- Bài hát + stem nhạc nằm trong `Songs/` (mỗi bài một thư mục, kèm `song.json`). Setlist nằm trong `Setlists/`.
- **KHÔNG xóa thư mục `bin/` và `obj/` nếu bạn không muốn build lại từ đầu** (và một số dữ liệu/log sinh ra lúc chạy có thể nằm trong các thư mục này tùy cấu hình). Trong phiên T1 này, việc xóa `bin/obj` được thực hiện CÓ CHỦ ĐÍCH theo lệnh của chủ sản phẩm để build sạch — xem ghi chú ở PROGRESS.md. Spec T1 gốc ghi "không dọn bin/obj"; lệnh trực tiếp của chủ sản phẩm yêu cầu dọn, nên đã dọn và ghi rõ.
- 25 file `.mp3` (~108MB) đang được git track (sẽ xử lý ở task T5). Không tự ý xóa khỏi đĩa.

---

## 4. Test tự động: có hay không, lệnh chạy

- **CÓ** project test: `WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` (`net8.0`, xUnit).
- Quy mô: **71 file test, 764 thuộc tính `[Fact]`/`[Theory]`**.
- Project test tại HEAD **chỉ tham chiếu `WorshipPlayer.Core`** (một `ProjectReference` duy nhất), test phần Core và các service thuần (grouping/timing/mapper/reconciler, Bible, scripture...).
- Lệnh chạy (trên Windows):
```powershell
# Chạy toàn bộ test trong solution
dotnet test

# Hoặc chạy riêng project test
dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj
```

> **Lưu ý:** `dotnet test` không kèm tham số project sẽ thao tác trên toàn solution — nên trên Linux nó dừng ngay ở project `WorshipPlayer.UI` (`net8.0-windows`) với lỗi `NETSDK1100`, chưa kịp chạy test nào. Trên Windows, hoặc khi chỉ định thẳng `WorshipPlayer.Tests.csproj`, test sẽ chạy. Kết quả lệnh nguyên văn tại HEAD được ghi trong `PROGRESS.md` (nhánh main).
