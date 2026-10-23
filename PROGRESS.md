# PROGRESS — Nhật ký làm việc của Kiro (Kiro cập nhật sau MỖI task, Muse đọc file này khi nghiệm thu)

## Trạng thái hiện tại
- **ĐỢT 2 ĐANG CHẠY — CHẾ ĐỘ CLAUDE-TỰ-CODE (từ 2026-10-23 09:51 ICT):** phiên Kiro Web hết hạn đăng nhập, chủ sản phẩm ra lệnh trực tiếp không chờ thêm → Muse tự code đợt 2 từ T1 (thông báo tại Issue #4, comment 6356247276). Kiro có phiên/credit trở lại thì bàn giao tiếp từ task đang dở — ĐỌC mục này + nhật ký đợt 2 bên dưới để biết điểm tiếp quản, KHÔNG làm trùng task Muse đã bắt đầu.
- Spec đợt 2: `.kiro/specs/dot-2-trinh-chieu-chuan-worshiptools` (requirements R1–R9, tasks T1–T17, chế độ liên tục). Đường cơ sở đầu đợt 2: 856 test PASS, build 0 error (tại `375b532`). Chuẩn SQLite bắt buộc: comment 6355687580 trên Issue #4.
- **[dot2-T1] XONG** (commit `3ca3275` trên kiro-current, Muse tự code): nền riêng theo item + ItemBackgroundResolver + nối render OutputWindow. Build 0 error / 8 warning, test **871/871 PASS** (856 cũ + 15 mới). Nghiệm thu ĐẠT tại Issue #4 (comment 6356585028).
- **[dot2-T2] XONG** (commit `876b8e5` trên kiro-current, Muse tự code): Inherit Background — chuỗi kế thừa + bulk apply + nối render OutputWindow + icon ↩ trong danh sách service. Build 0 error / 8 warning, test **887/887 PASS** (871 cũ + 16 mới). Task tiếp theo: **T3** (Auto Advance: chốt điều kiện hợp lệ Lyrics/Scripture).
- **[dot2-T3] XONG** (commit `d4d3b2c` trên kiro-current, Muse tự code): Auto Advance — chốt điều kiện hợp lệ Lyrics/Scripture (`IsEligible` ở Core + guard trong `AutoAdvanceResolver.Resolve` và `PresenterEngine` + ghi chú/vô hiệu hoá control ở `ItemSettingsPanel`). Build 0 error / 8 warning, test **898/898 PASS** (887 cũ + 11 mới). Task tiếp theo: **T4** (Auto Advance: trigger khi video nền chạy hết).
- **[dot2-T4] XONG** (commit `980ec01` trên kiro-current, Muse tự code): Auto Advance — trigger VideoEnd hoạt động thật (`AutoAdvanceTrigger` + overload `Resolve(trigger)` ở Core; `VideoBackgroundService.PlaybackEnded`; `OutputWindow.BackgroundVideoEnded` + item VideoEnd phát video 1 lần thay vì loop; `PresenterEngine.NotifyCurrentItemVideoEnded`; `PresenterIntegration` nối cả 2 tín hiệu video-hết vào engine). Build 0 error / 8 warning, test **908/908 PASS** (898 cũ + 10 mới). Kiểm chứng tay trên app Windows: HOÃN tới bước bàn giao (VM Linux không chạy được WPF/LibVLC) — xem nhật ký T4. Task tiếp theo: **T5** (Auto Advance: đếm ngược trên khung service).
- **[dot2-T5] XONG** (commit `870f336` trên kiro-current, Muse tự code): Auto Advance — đếm ngược trên khung service (`AutoAdvanceCountdown` thuần ở Core; `PresenterEngine` phát `AutoAdvanceCountdownChanged` + thuộc tính countdown, ticker 250ms, huỷ ngay khi đổi item/không hợp lệ; `OperatorWindow` thanh đếm ngược ở đầu khung danh sách service + icon ⏱ trên item đang đếm ngược). Build 0 error / 8 warning, test **923/923 PASS** (908 cũ + 15 mới). Kiểm chứng tay trên app Windows: HOÃN tới bước bàn giao (VM Linux không chạy được WPF) — xem nhật ký T5. Task tiếp theo: **T6** (Headers hoàn thiện trong Service Builder).
- **[dot2-T6] XONG** (commit `be25224` trên kiro-current, Muse tự code): Headers hoàn thiện trong Service Builder (R4) — logic thuần `ServiceGroupAssignment` ở Core (Assign/RemoveItem/DeleteGroup/MoveGroup/Normalize), `ServiceOrderManager` uỷ quyền + `MoveGroup` + LoadAsync tự chuẩn hoá id mồ côi/trùng; OperatorWindow thêm "Move Group Up/Down". Build 0 error / 8 warning, test **935/935 PASS** (923 cũ + 12 mới). Kiểm chứng tay trên app Windows: HOÃN tới bước bàn giao (VM Linux không chạy được WPF) — xem nhật ký T6. Task tiếp theo: **T7** (Slide Labels ở Core).
- Spec đợt 1: `.kiro/specs/dot-1-khoa-bang-chung` (đợt 1 — "Khóa bằng chứng & nối mắt xích cuối của Slides")
- Chế độ: **LIÊN TỤC** (Muse ra lệnh ở Issue #3) — làm nối tiếp T3 → T4 → T5, không dừng chờ nghiệm thu giữa các task. T1 đã ĐẠT (Muse tự build sạch + 854/854 test PASS tại b245e07). T2 HOÃN (cần tay chủ sản phẩm trên phần cứng cảm ứng thật). **T3 XONG** (commit `0ddff03`, build sạch + 856/856 test PASS). **T4 XONG** (bảng 7 panel/dialog ở mục ĐỀ XUẤT; chỉ phân tích, không code). **T5 XONG** (commit `375b532`: gỡ track 25 mp3 + bật .gitignore + xóa latest-changes.patch; build sạch + 856/856 test PASS). **HẾT hàng đợi T3–T5** — không còn task nào trong chế độ liên tục (T2 hoãn).
- NETSDK1100 đã được giải: **mọi lệnh build/test trên sandbox Linux luôn thêm cờ `-p:EnableWindowsTargeting=true`** (theo Issue #2). Việc 0 đã build sạch + 854/854 test PASS trên Linux (xem nhật ký Việc 0).
- Commit đã build mới nhất: `b86df8b` (nhánh kiro-current, HEAD khi chạy Việc 0 — code đồng nhất b245e07, chỉ thêm BAO-CAO.md).
- Lần cập nhật cuối: 2026-10-23

## Nhật ký theo task
<!-- Mẫu mỗi mục:
### [task T1] <tên task> — <ngày>
- Đã làm: ...
- Lệnh build/test đã chạy: ...
- Kết quả: pass/fail (số liệu cụ thể) ...
- Tiêu chí nghiệm thu: đạt ... / chưa đạt ...
- Commit: <hash + thông điệp>
-->

### [dot2-T1] Nền riêng theo từng ServiceItem (R1): model + resolver ở Core — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE kích hoạt theo lệnh trực tiếp của chủ sản phẩm (Issue #4, comment 6356247276).
- Commit code (nhánh kiro-current): `3ca3275` — "[dot2-T1] Nen rieng theo tung ServiceItem (R1)...". 5 file: 3 sửa + 2 mới.
- **Đã làm:**
  1. `WorshipPlayer.Core/Models/ServiceOrder.cs`: thêm vào `ServiceItem` — `BackgroundKind` (enum mới `ItemBackgroundKind` {Theme, None, Color, Image, Video}), `BackgroundColor`, `BackgroundFilePath` (đều nullable, `[JsonPropertyName]`, bỏ qua khi null theo quy ước WhenWritingNull → file cũ không đổi). Trường `BackgroundType?` cũ GIỮ NGUYÊN, chỉ thêm ghi chú ánh xạ tương thích — không ghi đè dữ liệu cũ khi nạp.
  2. `WorshipPlayer.Core/Services/ItemBackgroundResolver.cs` (MỚI, thuần): `GetItemKind` (kind tường minh thắng; không có thì ánh xạ legacy: SolidColor→Color, Image→Image, Video→Video, Gradient→Theme vì gradient không có tương đương cấp item) + `Resolve(item, theme)` → `ResolvedItemBackground` {Kind, Color, FilePath, Source Item/Theme}. Fallback giá trị: Color thiếu màu riêng → màu nền theme; Image/Video thiếu BackgroundFilePath → `MediaPath` legacy (nơi editor cũ lưu file nền). Đây là nơi DUY NHẤT tính nền hiệu dụng cấp item (inherit thuộc T2, chưa làm).
  3. Nối render: `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs` — tách khối nền của `ApplyTheme` thành `ApplyBackground(theme)`: Source=Theme → chạy nguyên khối switch theme cũ; Source=Item → render theo resolved (None → đen; Color → màu đã parse, sai mã → đen + log; Image/Video → file qua `ResolveImagePath`, Video dùng lại `VideoBackgroundService` qua `StartMotionBackground`; file thiếu/lỗi → nền đen + Debug log, foreground không đụng). Thêm `SetCurrentItem(ServiceItem?)`; `OperatorWindow.OnItemChanged` gọi `_outputWindow?.SetCurrentItem(e.NewItem)` sau khối định tuyến video.
  4. Ngoài phạm vi (đúng spec): KHÔNG thêm trường nền vào `Song`, KHÔNG đổi `SlideTheme`, KHÔNG làm UI chọn nền, KHÔNG làm inherit.
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`) trên bản Linux của Muse (không dùng `dotnet test` — vstest không chạy được trên VM này, xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 871, Errors: 0, Failed: 0, Skipped: 0
  ```
  → 856 test cơ sở + **15 test mới** đều PASS.
- **Tiêu chí nghiệm thu T1:**
  - [x] Unit test mới `ItemBackgroundResolverTests` (13 test): không đặt gì → theme; None; Color kèm mã màu; Color thiếu màu → màu theme; Image/Video kèm đường dẫn; Image thiếu đường dẫn riêng → MediaPath; kind tường minh thắng legacy.
  - [x] Ánh xạ file cũ chỉ có `BackgroundType`: SolidColor→Color (màu theme), Image→Image + MediaPath, Video→Video + MediaPath, Gradient→Theme.
  - [x] Test tương thích ngược `ItemBackgroundPersistenceTests` (2 test): JSON service cũ (không trường nền mới) nạp qua `ServiceOrderManager` → các trường mới null, resolve cho nền = theme (item thường) / đúng ánh xạ legacy (item legacy); trường mới round-trip qua save/load nguyên vẹn.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 871/871.
- Commit: `3ca3275` (kiro-current) + commit PROGRESS này (main).

### [dot2-T2] Inherit Background (R2): chuỗi kế thừa + bulk apply — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE (Issue #4, comment 6356247276).
- Commit code (nhánh kiro-current): `876b8e5` — "[dot2-T2] Inherit Background (R2)...". 9 file: 7 sửa + 2 mới.
- **Đã làm:**
  1. `WorshipPlayer.Core/Models/ServiceOrder.cs`: bổ sung `Inherit` vào `ItemBackgroundKind` (thêm cuối enum; file cũ không bao giờ có giá trị này).
  2. `WorshipPlayer.Core/Services/InheritBackgroundResolver.cs` (MỚI, thuần): `IsInherit(item)` (chỉ kind tường minh Inherit; legacy BackgroundType không bao giờ là Inherit) + `ResolveItem(items, index, theme)` (đi ngược từ index lên, bỏ qua các item Inherit khác, tới item đầu tiên có nền thật → render nền đã resolve của item đó; không có → theme; item đầu danh sách Inherit → theme) + `ResolveService(items, theme)` (toàn list theo thứ tự tại thời điểm gọi; list rỗng → rỗng, không lỗi). KHÔNG ghi kết quả resolve vào item con — sắp xếp/xoá/đổi nguồn được nhận ở lần resolve sau.
  3. Nối render: `OutputWindow.xaml.cs` — thêm `SetCurrentItem(item, serviceItems)` + `ResolveCurrentBackground()` (có list và tìm được item → resolve qua chuỗi Inherit, khớp theo tham chiếu rồi theo Id; không tìm được → resolve cấp item như T1); `ApplyBackground` dùng hàm này. `OperatorWindow.OnItemChanged` giờ gọi `SetCurrentItem(e.NewItem, _engine?.CurrentServiceOrder?.Items)`.
  4. Bulk edit: `BulkEditService` — thêm `BulkEditOptions.ApplyBackgroundInherit` (thắng lựa chọn legacy BackgroundType trong cùng lần apply; legacy BackgroundType được giữ nguyên để khôi phục khi item rời Inherit); `BulkEditPanel.xaml` thêm lựa chọn "Inherit Background" vào combo Set Background; code-behind ánh xạ lựa chọn này thành `ApplyBackgroundInherit = true`.
  5. Hiển thị trực quan: `ServiceItemDisplay.InheritBadgeVisibility` + TextBlock "↩" (tooltip "Inherits background from the item above") cạnh tên item trong danh sách service của OperatorWindow.
  6. Ngoài phạm vi (đúng spec): KHÔNG inherit từ item không liên tục (chỉ chuỗi "ngay phía trên"); KHÔNG cơ chế chọn nguồn inherit tuỳ ý.
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  (8 warning đã có sẵn ở WorshipPlayer.UI từ trước đợt 2, không thuộc file T2.)
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`, `LD_LIBRARY_PATH` tới `runtimes/linux-x64/native` để SQLite native nạp được — xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 887, Errors: 0, Failed: 0, Skipped: 0
  ```
  → 871 test cũ + **16 test mới** đều PASS.
- **Tiêu chí nghiệm thu T2:**
  - [x] `InheritBackgroundResolverTests` (12 test): chuỗi 1 item; chuỗi 3 item cùng kế thừa 1 nền; đổi nền item nguồn → chuỗi đổi theo khi resolve lại (và không ghi vào item con); item đầu danh sách Inherit → theme; xoá item nguồn → chuỗi resolve lại đúng; list rỗng → không lỗi; item không Inherit cắt chuỗi đúng; kế thừa nguồn None; bỏ qua nhiều item Inherit tới nguồn thật gần nhất; index ngoài phạm vi → ném ArgumentOutOfRangeException; `IsInherit` chỉ true với kind tường minh Inherit.
  - [x] `BulkEditInheritBackgroundTests` (4 test): áp Inherit chỉ đổi đúng item được chọn; các trường khác (transition, auto-advance, legacy BackgroundType, title) không đổi; Inherit thắng lựa chọn legacy trong cùng lần apply; không opt-in → BackgroundKind không đụng.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 887/887.
- Commit: `876b8e5` (kiro-current) + commit PROGRESS này (main).

### [dot2-T3] Auto Advance: chốt điều kiện hợp lệ Lyrics/Scripture (R3, phần 1) — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE (Issue #4, comment 6356247276). Code hoàn thành tại commit local `dc4c90c`; vòng hoàn tất này Muse KIỂM CHỨNG ĐỘC LẬP lại toàn bộ (build + full test) tại đúng commit đó rồi mới đẩy lên remote.
- Commit code (nhánh kiro-current): `d4d3b2c` — "[dot2-T3] Auto Advance: chot dieu kien hop le Lyrics/Scripture (R3 phan 1)...". 5 file sửa, không file mới.
- **Đã làm:**
  1. `WorshipPlayer.Core/Services/AutoAdvanceResolver.cs`: thêm hàm thuần `IsEligible(ServiceItem?)` — null → false; `Type == Slides` → true; `ForegroundType` Text/Media/Blank → true; Lyrics/Scripture → false. `Resolve` dùng guard này ngay sau khi lấy item hiện tại: item không hợp lệ → `Decision.None` dù `AutoAdvanceAfterCondition = Timer`.
  2. `WorshipPlayer.Presenter/Services/PresenterEngine.cs` (`RestartAutoAdvanceTimer`): không khởi động timer cho item không hợp lệ (cùng quy tắc với resolver).
  3. UI `ItemSettingsPanel` (xaml + code-behind): item không hợp lệ → hiện ghi chú Auto Advance không khả dụng (lý do: foreground Lyrics/Scripture) và vô hiệu hoá các control auto-advance (GoTo, AfterCondition, GoToTarget, Delay); Transition Fade Time vẫn sửa được (không thuộc auto-advance).
  4. Ngoài phạm vi (đúng spec): KHÔNG đụng trigger VideoEnd (thuộc T4); KHÔNG sửa `SlideAutoAdvanceMap`; KHÔNG đổi hành vi Manual mặc định.
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN, kiểm chứng độc lập):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  (8 warning đã có sẵn từ trước đợt 2, không thuộc file T3.)
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`, `LD_LIBRARY_PATH` tới `runtimes/linux-x64/native` — xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN, kiểm chứng độc lập):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 898, Errors: 0, Failed: 0, Skipped: 0, Time: 241.010s
  ```
  → 887 test cũ + **11 test mới** đều PASS.
- **Tiêu chí nghiệm thu T3:**
  - [x] Test mới trong `AutoAdvanceResolverTests` (11 case): `IsEligible_NullItem_ReturnsFalse`; `IsEligible_LyricsOrScripture_ReturnsFalse` (Theory 2 case); `IsEligible_TextMediaBlank_ReturnsTrue` (Theory 3 case); `IsEligible_SlidesTypeWithLyricsForeground_ReturnsTrue`; `Timer_WithLyricsItem_ReturnsNone`; `Timer_WithScriptureItem_ReturnsNone_EvenSpecificTarget`; `Timer_WithTextItem_StillAdvancesNextItem`; `Timer_WithSlidesTypeItem_AdvancesNextItem`.
  - [x] Không hồi quy: toàn bộ test AutoAdvance cũ (NullSettings, ManualCondition, Timer NextItem/NextSlide/SpecificItem/Loop, DeferredMediaConditions, UnknownCurrentId, NullOrder...) vẫn pass trong tổng 898/898.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 898/898.
- Commit: `d4d3b2c` (kiro-current) + commit PROGRESS này (main).

### [dot2-T4] Auto Advance: trigger khi video nền chạy hết (R3, phần 2) — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE (Issue #4, comment 6356247276).
- Commit code (nhánh kiro-current): `980ec01` — "[dot2-T4] Auto Advance: trigger khi video nen chay het (R3 phan 2)...". 6 file sửa, không file mới.
- **Đã làm:**
  1. `WorshipPlayer.Core/Services/AutoAdvanceResolver.cs`: enum mới `AutoAdvanceTrigger { Timer, VideoEnd }` + overload `Resolve(order, currentItemId, trigger)`. Chữ ký cũ 2 tham số GIỮ NGUYÊN, uỷ thác `trigger = Timer` (không phá test cũ). Điều kiện của item phải KHỚP trigger mới được quyết định: Timer↔Timer, VideoEnd↔VideoEnd; `AudioEnd` giữ DEFERRED (None trên mọi trigger — thuộc R9/đợt sau); Manual không bao giờ kích hoạt. Guard `IsEligible` của T3 chạy trước cho cả hai trigger. Nhánh đích `AutoAdvanceGoTo` (NextItem/NextSlide, SpecificItem kèm fallback NextItem khi đích sai/thiếu, Loop về chính item, item cuối + NextItem → None) dùng chung cho VideoEnd như nhánh Timer.
  2. `WorshipPlayer.Presenter/Services/VideoBackgroundService.cs`: event mới `PlaybackEnded` — chỉ bắn khi video KHÔNG loop chạy tới cuối (`EndReached` với `_shouldLoop = false`); video loop vẫn tự phát lại theo cơ chế cũ và không bắn event.
  3. `WorshipPlayer.Presenter/Views/OutputWindow.xaml.cs`: event mới `BackgroundVideoEnded` (được nâng lên UI thread qua Dispatcher, cùng mẫu `VideoPlaybackEnded` hiện có); `StartMotionBackground` nay phát video 1 LẦN khi item hiện tại đặt điều kiện VideoEnd (`loop: !CurrentItemWaitsForVideoEnd`), mọi item khác giữ nguyên loop như trước T4. Huỷ đăng ký event trong `OnClosed`.
  4. `WorshipPlayer.Presenter/Services/PresenterEngine.cs`: `FireAutoAdvance(AutoAdvanceTrigger)` — timer gọi với `Timer`; thêm `NotifyCurrentItemVideoEnded()` công khai cho trigger VideoEnd.
  5. `WorshipPlayer.UI/Services/PresenterIntegration.cs`: tại cả 2 điểm tạo `OutputWindow` (ApplyDisplaySettings + HandleGoLiveSingleMonitor), đăng ký `VideoPlaybackEnded` (video toàn màn hình chạy hết — event này có sẵn từ trước nhưng CHƯA từng được ai nối vào engine) + `BackgroundVideoEnded` → `PresenterEngine.NotifyCurrentItemVideoEnded()`.
  6. Hệ quả đúng spec: item đặt VideoEnd nhưng không chạy video nào → không có tín hiệu ended nào → không tự chuyển, KHÔNG rơi về Timer (`RestartAutoAdvanceTimer` chỉ nạp timer cho điều kiện Timer; resolver VideoEnd + trigger Timer → None, có test chốt).
  7. Ngoài phạm vi (đúng spec): KHÔNG làm loop video (giữ nguyên cơ chế loop hiện có); KHÔNG dùng `AudioEnd` (giữ deferred).
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  (8 warning đã có sẵn từ trước đợt 2, không thuộc file T4.)
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`, `LD_LIBRARY_PATH` tới `runtimes/linux-x64/native` — xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 908, Errors: 0, Failed: 0, Skipped: 0, Time: 166.824s
  ```
  → 898 test cũ + **10 test mới** đều PASS.
- **Tiêu chí nghiệm thu T4:**
  - [x] Unit test resolver mới (10 case trong `AutoAdvanceResolverTests`): `VideoEnd_NextItem_WithVideoEndTrigger_ReturnsNextItem`; `VideoEnd_SpecificItem_WithVideoEndTrigger_ReturnsGoToTarget`; `VideoEnd_Loop_WithVideoEndTrigger_ReturnsGoToCurrentItem`; `VideoEnd_SpecificItem_InvalidTarget_WithVideoEndTrigger_FallsBackToNextItem`; `VideoEnd_NextItem_OnLastItem_WithVideoEndTrigger_ReturnsNone`; `VideoEnd_WithTimerTrigger_ReturnsNone_NoFallbackToTimer`; `Timer_WithVideoEndTrigger_ReturnsNone`; `AudioEnd_WithVideoEndTrigger_StillDeferred_ReturnsNone`; `VideoEnd_WithLyricsItem_ReturnsNone`; `VideoEnd_WithScriptureItem_ReturnsNone_EvenSpecificTarget`.
  - [x] Test cũ pass nguyên vẹn: theory `DeferredMediaConditions_ReturnNone` cũ gọi chữ ký 2 tham số (đường Timer) — VideoEnd trên đường Timer vẫn → None như trước T4; toàn bộ test AutoAdvance/T3 giữ nguyên trong tổng 908/908.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 908/908.
  - [ ] **KIỂM CHỨNG TAY — CHƯA LÀM, HOÃN SANG MÁY WINDOWS:** spec yêu cầu dựng item nền video ngắn đặt VideoEnd → chiếu → quan sát tự chuyển khi video hết. Môi trường làm việc hiện tại là VM Linux (chỉ biên dịch được WPF qua EnableWindowsTargeting, không chạy được app WPF + LibVLC thật) nên KHÔNG thực hiện được và KHÔNG ghi kết quả quan sát nào. Việc này để lại cho máy Windows của chủ sản phẩm ở bước bàn giao đợt 2: tạo 1 item Media/Text gắn nền video ngắn (~5 giây), đặt Auto Advance After = VideoEnd, GoTo = Next Item, chiếu và xác nhận item tự chuyển đúng lúc video kết thúc; đồng thời xác nhận item VideoEnd nhưng không gắn video thì đứng yên.
- Commit: `980ec01` (kiro-current) + commit PROGRESS này (main).

### [dot2-T5] Auto Advance: đếm ngược trên khung service (R3, phần 3) — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE (Issue #4, comment 6356247276).
- Commit code (nhánh kiro-current): `870f336` — "[dot2-T5] Auto Advance: dem nguoc tren khung service (R3 phan 3)...". 5 file: 3 sửa + 2 mới.
- **Đã làm:**
  1. `WorshipPlayer.Core/Services/AutoAdvanceCountdown.cs` (MỚI, thuần): `EffectiveDelayMs` (delay ≤ 0 → mặc định `DefaultDelayMs = 5000` — CÙNG quy tắc engine vẫn dùng khi nạp timer, nay dùng chung một chỗ để hiển thị và timer không bao giờ lệch nhau); `RemainingMs(delay, elapsed)` và `RemainingMs(configuredDelay, startedAtUtc, nowUtc)` — kẹp không âm, hết giờ → 0, đồng hồ chạy ngược → coi như vừa bắt đầu; `IsFinished`; `Format` dạng `m:ss` làm tròn LÊN (vừa bắt đầu hiện đủ delay, chạm "0:00" đúng lúc hết giờ). Logic này TÁCH BIỆT hẳn `CountdownService` (đếm ngược giờ bắt đầu buổi lễ) — không đụng vào service đó, đúng phạm vi KHÔNG làm.
  2. `WorshipPlayer.Presenter/Services/PresenterEngine.cs`: khi `RestartAutoAdvanceTimer` nạp timer cho item (đường Timer), engine đồng thời mở một ticker 250ms chỉ để BÁO cáo: sự kiện mới `AutoAdvanceCountdownChanged` (`AutoAdvanceCountdownEventArgs` {ItemId, RemainingMs, IsActive}) bắn lúc bắt đầu, mỗi tick, và lúc huỷ/kết thúc; thêm thuộc tính `AutoAdvanceCountdownItemId` + `AutoAdvanceCountdownRemainingMs` (tính trực tiếp từ mốc bắt đầu). `RestartAutoAdvanceTimer` chạy trên MỌI lần đổi item (kể cả chuyển tay) và luôn `CancelAutoAdvanceCountdown()` trước → đếm ngược của item cũ bị huỷ NGAY khi đổi item hoặc khi item mới không hợp lệ theo guard T3/không dùng điều kiện Timer. Khi đếm ngược chạm 0 mà quyết định advance là None (vd item cuối) thì ticker tự đóng đếm ngược để UI không treo số 0. Timer một lần cũ vẫn là cơ chế DUY NHẤT thực hiện chuyển item — ticker không tự chuyển gì. `Dispose` dọn cả ticker.
  3. `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` + `.xaml.cs`: thanh đếm ngược `AutoAdvanceCountdownBar` ("⏱ Auto-advance in m:ss", mặc định ẩn) đặt ở ĐẦU khung danh sách service (hàng mới ngay trên `ServiceOrderList`, trong cùng sidebar); hàng item đang đếm ngược hiện icon ⏱ cạnh tên — `ServiceItemDisplay` nay có `CountdownBadgeVisibility` kèm `INotifyPropertyChanged` cho riêng badge này, `MakeItemRow` cũng gắn sẵn cờ từ `engine.AutoAdvanceCountdownItemId` để danh sách dựng lại không mất dấu. Handler `OnAutoAdvanceCountdownChanged` marshal về UI thread bằng `Dispatcher.Invoke` như các handler engine khác; sự kiện inactive → ẩn thanh + xoá mọi icon ngay. KHÔNG hiển thị đếm ngược trên OutputWindow (đúng phạm vi KHÔNG làm).
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  (8 warning đã có sẵn từ trước đợt 2, không thuộc file T5.)
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`, `LD_LIBRARY_PATH` tới `runtimes/linux-x64/native` — xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 923, Errors: 0, Failed: 0, Skipped: 0, Time: 131.163s
  ```
  → 908 test cũ + **15 test mới** đều PASS.
- **Tiêu chí nghiệm thu T5:**
  - [x] Unit test mới cho logic đếm ngược thuần (`AutoAdvanceCountdownTests`, 15 case): delay dương dùng đúng cấu hình; delay 0/âm → mặc định 5000ms (cả ở dạng elapsed lẫn timestamp); remaining giảm đúng theo thời gian trôi (0/3000/9999ms → 10000/7000/1); đúng mốc hết giờ → 0; quá giờ → kẹp 0 không âm; delay không dương → 0; đồng hồ chạy ngược → coi như vừa bắt đầu; `IsFinished` trước mốc = false, tại/sau mốc = true; `Format`: 0/âm → "0:00", 10000 → "0:10", 9000 → "0:09", 65000 → "1:05", 600000 → "10:00", phần lẻ giây làm tròn lên (9500 → "0:10", 1 → "0:01").
  - [x] Test cũ pass nguyên vẹn: toàn bộ 908 test trước T5 (gồm AutoAdvanceResolver/T3/T4, CountdownService của đếm ngược giờ lễ — không bị đụng) đều PASS trong tổng 923/923.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 923/923.
  - [ ] **KIỂM CHỨNG TAY — CHƯA LÀM, HOÃN SANG MÁY WINDOWS:** spec yêu cầu đặt item Text với Timer 10s → chiếu → thấy đếm ngược ở đầu khung service, chuyển item bằng tay giữa chừng → đếm ngược biến mất ngay. Môi trường làm việc hiện tại là VM Linux (chỉ biên dịch được WPF qua EnableWindowsTargeting, không chạy được app WPF thật) nên KHÔNG thực hiện được và KHÔNG ghi kết quả quan sát nào. Việc này để lại cho máy Windows của chủ sản phẩm ở bước bàn giao đợt 2: (1) tạo/chọn 1 item Text, đặt Auto Advance After = Timer, Delay = 10 giây, GoTo = Next Item; (2) chiếu item đó và xác nhận thanh "⏱ Auto-advance in 0:10" xuất hiện ở đầu khung SERVICE ORDER, đếm lùi từng giây, icon ⏱ hiện cạnh tên item trong danh sách; (3) khi hết giờ, item tự chuyển và đếm ngược của item cũ biến mất (item kế có Timer thì đếm ngược mới bắt đầu); (4) chiếu lại item đó rồi chuyển item BẰNG TAY giữa chừng → đếm ngược biến mất ngay lập tức.
- Commit: `870f336` (kiro-current) + commit PROGRESS này (main).

### [dot2-T6] Headers hoàn thiện trong Service Builder (R4) — 2026-10-23
- **Người làm: Muse tự code (Kiro không làm việc được)** — chế độ CLAUDE-TỰ-CODE (Issue #4, comment 6356247276). Claim T6 ghi tại watch-state lúc ~12:21 ICT trước khi code (chống trùng việc); remote đối chiếu lúc bắt đầu: kiro-current HEAD `870f336`, main HEAD `1a9d0aa`, đường cơ sở 923 test.
- Commit code (nhánh kiro-current): `be25224` — "[dot2-T6] Headers hoan thien trong Service Builder (R4)...". 5 file: 2 mới + 3 sửa.
- **BÁO CÁO BƯỚC 1 — trạng thái thật của headers tại HEAD `870f336` (kiểm tra TRƯỚC khi sửa):**
  1. **Model đã có và đủ dùng (không đổi):** `ServiceOrder.Groups: List<ServiceGroup>`; `ServiceGroup` {Id, Name, Order, Items = danh sách id item, IsCollapsed, OrganizationId} — lưu trong JSON service; SQLite chỉ là bản gương của nguyên JSON.
  2. **Core đã có sẵn (roadmap "C2", trong `ServiceOrderManager`):** `CreateGroup` (Order = số group hiện có), `RenameGroup`, `DeleteGroup` (xoá group KHÔNG xoá item — item thành không-nhóm; Order nén lại), `AssignItemToGroup` (gỡ id khỏi MỌI group trước rồi mới thêm vào group đích → một item tối đa một group), `SetGroupCollapsed`, `GetUngroupedItemIds`; `RemoveItem` đã dọn id item bị xoá khỏi mọi group (không id mồ côi). Đã có sẵn 12 unit test (`ServiceOrderManagerGroupTests`).
  3. **UI đã nối dây thật — tại sidebar của `OperatorWindow` (bề mặt Service Builder):** nút header "§ Group" + mục context "Add Group…" → `CreateGroup`; menu chuột phải trên hàng header: Rename Group…, Delete Group; submenu "Move to Group ▸" (gồm cả "Ungrouped") → `AssignItemToGroup`; chevron trên hàng header → `SetGroupCollapsed` và lưu qua `SaveServiceOrderAsync` (trạng thái collapse nằm trong file service); render danh sách theo group: hàng header + item (group thu gọn chỉ hiện header) + mục "Ungrouped" cho item không-nhóm.
  4. **Header không lên output — bằng cấu trúc:** header KHÔNG phải `ServiceItem` (chỉ là `ServiceGroup` + hàng hiển thị `ServiceItemDisplay.IsGroupHeader` trong OperatorWindow). `PresenterEngine.NextItemAction/PreviousItemAction` uỷ quyền thẳng cho `ServiceOrderManager.NextItem/PreviousItem` — bước theo INDEX trên `ServiceOrder.Items`, danh sách này không chứa header → điều hướng không bao giờ "đi vào" header và output chỉ nhận `ServiceItem` thật.
  5. **KHOẢNG TRỐNG tìm thấy (phần T6 phải làm):** (a) **Chưa có sắp xếp lại header** — R4.1 yêu cầu "sắp xếp lại header" nhưng Core không có MoveGroup, UI không có lên/xuống cho group; (b) **LoadAsync không chuẩn hoá dữ liệu group** — file service bị sửa tay/bản cũ chứa id mồ côi (id không còn trong Items) hoặc một id nằm ở 2 group thì nạp vào vẫn giữ nguyên mất nhất quán; chưa có lớp logic thuần `ServiceGroupAssignment` như spec yêu cầu (logic đang nằm trong manager); (c) **`ServiceGroupPanel` MỒ CÔI** — khớp bảng đợt 1 T4: không XAML/code nào host nó, nó dùng VM riêng `ServiceGroupItem` (Name/IsExpanded/ItemIds) KHÔNG nối `ServiceGroup`/`ServiceOrder.Groups`, nút thu gọn chỉ đổi ký tự ▼/► không lưu `IsCollapsed`, rename/delete theo Name thay vì Id. Theo đúng lựa chọn spec cho phép ("dùng ServiceGroupPanel nếu đã nối dây, hoặc điểm vào hiện có"), T6 dùng điểm vào hiện có là OperatorWindow và để nguyên panel (không thuộc phạm vi sửa).
- **Đã làm:**
  1. `WorshipPlayer.Core/Services/ServiceGroupAssignment.cs` (MỚI, thuần, static) — NƠI DUY NHẤT sửa tư cách thành viên group, giữ 4 bất biến: item tối đa 1 group; không id mồ côi; `Order` luôn dày 0..n-1 sau mọi thay đổi cấu trúc; xoá group không bao giờ xoá item. Gồm `AssignItem` (gỡ khỏi mọi group → thêm vào đích; id lạ trả false), `RemoveItemFromAllGroups` (trả số mục đã gỡ), `DeleteGroup` (giữ item + nén Order), `MoveGroup` (sắp xếp lại theo Order hiện tại, chèn tại vị trí mới đã kẹp trong range, viết lại Order dày), `Normalize` (dọn id mồ côi + khử trùng — id ở nhiều group chỉ giữ ở group đầu tiên theo Order + nén Order; giữ nguyên tên/collapse), `CompactGroupOrder`.
  2. `WorshipPlayer.Core/Services/ServiceOrderManager.cs`: `AssignItemToGroup`/`DeleteGroup` uỷ quyền sang helper thuần (giữ nguyên chữ ký + ngữ nghĩa, 12 test cũ là bằng chứng không hồi quy); `RemoveItem` dùng `RemoveItemFromAllGroups`; thêm `MoveGroup(groupId, newIndex)`; `LoadAsync` gọi `ServiceGroupAssignment.Normalize` ngay sau deserialize (trước khi gán CurrentServiceOrder và trước khi ghi bản gương SQLite) — dữ liệu group từ file ngoài tự lành khi nạp.
  3. `WorshipPlayer.Presenter/Views/OperatorWindow.xaml` + `.xaml.cs`: thêm 2 mục context menu trên hàng header — "Move Group Up" / "Move Group Down" (sau Delete Group). `ServiceOrderContextMenu_Opened` chỉ bật cho hàng header thật, và tắt Up khi group đang đầu / tắt Down khi group đang cuối; handlers `MoveGroupUp_Click`/`MoveGroupDown_Click` → dùng chung `MoveSelectedGroupAsync(delta)` theo đúng pattern sẵn có: `MoveGroup` → `SaveServiceOrderAsync` → `RefreshServiceOrderList`. KHÔNG đụng render output, KHÔNG đổi model `ServiceGroup`, KHÔNG biến header thành item trình chiếu (đúng phạm vi KHÔNG làm).
- **Lệnh build:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
      8 Warning(s)
      0 Error(s)
  ```
  (8 warning đã có sẵn từ trước đợt 2, không thuộc file T6.)
- **Lệnh test:** build WorshipPlayer.Tests rồi chạy xUnit console runner (xunit.runner.console 2.9.2, `DOTNET_ROLL_FORWARD=LatestMajor`, `LD_LIBRARY_PATH` tới `runtimes/linux-x64/native` — xem AGENTS.md).
- **Kết quả test (NGUYÊN VĂN):**
  ```
  === TEST EXECUTION SUMMARY ===
     WorshipPlayer.Tests  Total: 935, Errors: 0, Failed: 0, Skipped: 0, Time: 179.026s
  ```
  → 923 test cũ + **12 test mới** đều PASS.
- **Tiêu chí nghiệm thu T6:**
  - [x] Báo cáo trạng thái thật (bước 1) ghi ở trên trước phần "Đã làm".
  - [x] Unit test mới cho logic gán (`ServiceGroupAssignmentTests`, 12 case): gán item đã có group → chuyển đúng, group cũ mất id; gán null → không-nhóm, id lạ → false; xoá item (RemoveItemFromAllGroups) → hết id mồ côi ở mọi group kể cả khi id bị liệt kê 2 nơi; xoá group → item còn nguyên và thành không-nhóm, Order nén; MoveGroup hoán đổi đúng vị trí + kẹp index + id lạ false; Normalize dọn id mồ côi, khử trùng giữ group đầu theo Order, nén Order lộn xộn (10/42 → 0/1) mà giữ IsCollapsed/tên; collapse flag round-trip qua JSON (serialize → deserialize với đúng JsonSerializerOptions của manager); tích hợp manager: `MoveGroup` qua ServiceOrderManager, và `LoadAsync` tự lành file bẩn (id "ghost" bị dọn, id trùng chỉ còn ở group đầu, Order 5/9 → 0/1, IsCollapsed giữ nguyên). 12 test group CŨ (`ServiceOrderManagerGroupTests`) vẫn PASS trong tổng 935 — đường uỷ quyền không hồi quy.
  - [x] Kiểm chứng header không xuất hiện trên output + Next/Previous đi đúng item thật — **bằng cấu trúc code + test** (mục 4 của báo cáo bước 1): header không phải ServiceItem; engine chuyển item theo index trên `Items`; danh sách hiển thị của OperatorWindow tách bạch hàng header (`IsGroupHeader`, `Item == null`) khỏi hàng item.
  - [x] Build 0 error; toàn bộ test (cũ + mới) pass: 935/935.
  - [ ] **KIỂM CHỨNG TAY TRÊN APP WINDOWS — CHƯA LÀM, HOÃN SANG BƯỚC BÀN GIAO (không bịa kết quả):** VM Linux chỉ biên dịch được WPF, không chạy được app thật. Để lại cho máy Windows của chủ sản phẩm: (1) mở 1 service, tạo 2 header, gán vài item bằng "Move to Group", thu gọn 1 header, lưu, mở lại → trạng thái collapse + thành viên giữ nguyên; (2) chuột phải header → Move Group Up/Down → thứ tự header đổi và giữ sau khi nạp lại; (3) đang chiếu, bấm Next/Previous qua vùng có header → output chuyển giữa các item thật, không bao giờ hiện header; (4) xoá 1 item đang thuộc group rồi nạp lại service → group không còn giữ id đó.
- Commit: `be25224` (kiro-current) + commit PROGRESS này (main).

### [task T5] Vệ sinh repo: gỡ track file nhạc + dọn file patch ở gốc — 2026-10-23
- Commit code (nhánh kiro-current): `375b532` — "[task T5] Ve sinh repo: go track 25 file .mp3 (giu tren dia), bat *.mp3/*.wav/*.flac trong .gitignore, xoa latest-changes.patch (noi dung da nam trong code)".
- **Lựa chọn đã ghi rõ:** Dùng `git rm --cached` (gỡ track, GIỮ file trên đĩa) + bật dòng audio trong `.gitignore` — KHÔNG dùng Git LFS. Lý do: spec T5 cấm viết lại lịch sử git (không filter-repo/force-push); LFS cho file đã có trong lịch sử cũng cần rewrite history để thực sự nhẹ repo → ngoài phạm vi. Mục tiêu đạt được trong phạm vi: ngừng track nhạc từ nay, chặn commit nhạc tương lai.
- **Đã làm:**
  1. `git rm --cached` cho toàn bộ **25 file .mp3** (`$(git ls-files "*.mp3")`) — 8 ở gốc + 17 trong `Songs/TheBlessing/`. File vẫn còn nguyên trên đĩa (xác minh: `ls *.mp3 Songs/TheBlessing/*.mp3` = 25 file).
  2. Sửa `.gitignore`: bỏ comment 3 dòng audio → bật `*.mp3`, `*.wav`, `*.flac`. Xác minh `git check-ignore 01_Click.mp3 Songs/TheBlessing/17_LeadVocal.mp3` → bị ignore (không bị add lại nhầm).
  3. `latest-changes.patch` ở gốc (~12KB, là bản export commit `8ac095a` ngày 2026-07-21 "separate timestamps from lyrics"): **nội dung ĐÃ nằm trong code hiện tại** nên xóa khỏi repo theo spec. Bằng chứng: method `RecalculateSlideTimings()` (phần ADDED của patch) có mặt tại `WorshipPlayer.UI/Views/WaveformEditorWindow.xaml.cs:184`; các symbol patch gỡ bỏ (`Strip {timestamp} brackets`, `SlideTimestampRegex`, `RecalculateSlideTimingFromText`, `ExtractSlideBlocks`, `SetSlideTimestamps`) grep toàn `WorshipPlayer.UI/` = KHÔNG còn → patch đã áp dụng xong, file thừa.
- **Dung lượng repo `.git` trước/sau:**
  - Trước: **97M** (`du -sh .git`).
  - Sau: **97M** (`du -sh .git`). KHÔNG đổi — đúng như kỳ vọng: `git rm --cached` chỉ gỡ khỏi index/commit mới, blob nhạc vẫn nằm trong lịch sử. Muốn `.git` nhẹ thật phải rewrite history (filter-repo) — việc đó spec CẤM ở T5. Lợi ích đạt được: các commit từ nay không kéo thêm nhạc; clone mới sau khi history được dọn (đợt khác, nếu Muse duyệt) mới nhẹ.
- **Lệnh build chuẩn:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
- **Lệnh test chuẩn:** `dotnet test WorshipPlayer.Tests --nologo -p:EnableWindowsTargeting=true`
- **Kết quả test (NGUYÊN VĂN):**
  ```
  Passed!  - Failed:     0, Passed:   856, Skipped:     0, Total:   856, Duration: 1 s - WorshipPlayer.Tests.dll (net8.0)
  ```
  → App vẫn build/test như trước (0 error / 8 warning, 856/856 PASS — gỡ track nhạc không ảnh hưởng build/test).
- **Tiêu chí nghiệm thu T5:**
  - [x] `git ls-files "*.mp3"` trả về **0** (xác minh sau commit). File nhạc vẫn còn trên đĩa (25 file).
  - [x] Thư mục gốc không còn `latest-changes.patch` (đã xóa; nội dung đã trong code).
  - [x] App vẫn build/test như trước (kết quả nguyên văn ở trên).
  - [x] Không viết lại lịch sử git, không xóa file nhạc trên đĩa, không đụng code.
- Commit: `375b532` (kiro-current) + commit PROGRESS/tasks.md này (main).

### [task T4] Bảng trạng thái thật cho 7 panel/dialog mồ côi — 2026-10-23
- **Loại việc:** CHỈ phân tích (theo spec T4: dừng ở bảng đề xuất, KHÔNG nối/xóa trong task này). Không chạm code → không có rủi ro hồi quy; build/test giữ nguyên baseline của Việc 0/T3 (0 error / 8 warning, 856/856 PASS).
- **Đã làm:** Rà 7 panel/dialog tại HEAD `0ddff03` bằng grep `new <X>` + host trong XAML (loại bỏ file `obj/*.g.cs`). Ghi bảng 7 dòng đầy đủ (tên — trạng thái thật có file:line — đề xuất a/b/c) vào mục **ĐỀ XUẤT** phía dưới.
- **Phát hiện chính:** Báo cáo nghiệm thu (N-4) liệt kê cả 7 là "mồ côi", nhưng rà lại tại HEAD thì **3/7 đã được dùng thật** (BulkEditPanel, ItemSettingsPanel, PrintExportDialog) — không còn mồ côi; **4/7 thật sự mồ côi**: ImportMediaDialog + ScriptureSlidePreview đã có bản thay thế đang chạy (đề xuất xóa), ServiceGroupPanel + MediaMetadataPanel là khung chưa nối và CHƯA có bản thay thế (đề xuất giữ).
- **Tiêu chí nghiệm thu T4:**
  - [x] Bảng đủ 7/7 mục, mỗi mục có căn cứ file:line/grep.
  - [x] Không mục nào ghi chung chung kiểu "đang phát triển".
- Commit: commit PROGRESS/tasks.md này (nhánh `main`). Không có commit code (đúng phạm vi).

### [task T3] Nối PresenterSettings.LinesPerSlide/SlideLeadMs vào SectionEditorWindow — 2026-10-23
- Commit code (nhánh kiro-current): `0ddff03` — "[task T3] Noi PresenterSettings.LinesPerSlide/SlideLeadMs vao SectionEditorWindow". 2 file đổi, +41 dòng.
- **Đã làm:**
  1. Trong `WorshipPlayer.UI/Views/MainWindow.xaml.cs`, handler `EditSections_Click` (dòng 608): sau khi tạo `var editor = new SectionEditorWindow(...)` và TRƯỚC `editor.ShowDialog()`, thêm 2 dòng:
     - `editor.LinesPerSlide = _presenterSettingsService.Settings.LinesPerSlide;`
     - `editor.SlideLeadMs = _presenterSettingsService.Settings.SlideLeadMs;`
     Dùng đúng field có sẵn `_presenterSettingsService` (khai báo dòng 23, nạp dòng 163). **Không** tạo cơ chế cấu hình mới, không thêm field/service.
  2. Đường đi đã xác minh: 2 property này sẵn chảy vào `AnalyzeSongAsync(linesPerSlide:, slideLeadMs:)` tại `SectionEditorWindow.xaml.cs:1179-1180` → pipeline Python `--lines-per-slide`/`--slide-lead-ms`. **Không** sửa SectionEditorWindow, SlideGroupingService, SlideAutoTimingService, không đổi mặc định 4/500, không sửa UI Settings.
  3. Thêm 2 test ở `WorshipPlayer.Tests/PresenterSettingsServiceTests.cs` ghim đúng hợp đồng mà phần nối dây dựa vào (SectionEditorWindow là WPF, không khởi tạo được trong test host net8.0 nên test tại Core — đúng mắt xích):
     - `FreshPresenterSettings_DefaultsAutoSlideConfig_To4And500`: PresenterSettings mới mặc định 4/500 (fallback khi người dùng không đổi gì).
     - `SaveThenReload_PreservesAutoSlideConfig_NonDefaultValues`: giá trị khác mặc định (2/1200) round-trip qua service. Cả hai FAIL nếu mặc định hoặc round-trip hỏng.
- **Lệnh build chuẩn:** `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  → 0 Error / 8 Warning (không có cảnh báo mới so với baseline — vẫn đúng 8 cảnh báo cũ).
- **Lệnh test chuẩn:** `dotnet test WorshipPlayer.Tests --nologo -p:EnableWindowsTargeting=true`
- **Kết quả test (NGUYÊN VĂN):**
  ```
  Passed!  - Failed:     0, Passed:   856, Skipped:     0, Total:   856, Duration: 1 s - WorshipPlayer.Tests.dll (net8.0)
  ```
  → 856/856 PASS (854 baseline + 2 test mới), 0 fail.
- **Kiểm chứng thủ công (cách kiểm):** Đổi `LinesPerSlide`/`SlideLeadMs` trong Presenter Settings (vd 2 / 1200) → mở Section Editor → `editor.LinesPerSlide`/`SlideLeadMs` lấy từ `_presenterSettingsService.Settings` → chảy xuống `AnalyzeSongAsync` → pipeline Python nhận đúng `--lines-per-slide 2 --slide-lead-ms 1200`. Không đổi Settings → PresenterSettings mặc định 4/500 → hành vi y như trước.
- **Tiêu chí nghiệm thu T3:**
  - [x] Đổi LinesPerSlide trong Settings rồi mở Section Editor: pipeline dùng đúng giá trị mới (nguồn `_presenterSettingsService.Settings`, chảy tới `--lines-per-slide`/`--slide-lead-ms`).
  - [x] Không set gì trong Settings thì hành vi như cũ (mặc định 4/500 — ghim bằng test).
  - [x] Build sạch (0 error / 8 warning cũ) + toàn bộ test pass (856/856).
- Commit: `0ddff03` (kiro-current) + commit PROGRESS/tasks.md này (main).

### [Việc 0] Chạy lại build + test bằng lệnh chuẩn có cờ EnableWindowsTargeting — 2026-10-23
- Commit chạy lệnh (nhánh kiro-current): `b86df8b` (code đồng nhất b245e07 — chỉ thêm BAO-CAO.md so với b245e07).
- Môi trường: **Linux sandbox**, .NET SDK `8.0.318`/`9.0.318` + `8.0.425`. Giải pháp NETSDK1100: thêm cờ `-p:EnableWindowsTargeting=true` (theo Issue #2).
- **Lệnh build chuẩn đã chạy:**
  `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo -p:EnableWindowsTargeting=true`
- **Kết quả build (NGUYÊN VĂN, dòng tổng kết):**
  ```
  Build succeeded.
      8 Warning(s)
      0 Error(s)
  ```
  → **0 Error / 8 Warning** (8 cảnh báo nhỏ có sẵn: CS4014/CS1998/CS0414 ở OperatorWindow + MediaLibraryPanel; CS8604/CS0414 ở ChordChartWindow + WaveformEditorWindow). Khớp baseline Muse tại b245e07.
- **Lệnh test chuẩn đã chạy:**
  `dotnet test WorshipPlayer.Tests --nologo -p:EnableWindowsTargeting=true`
- **Kết quả test (NGUYÊN VĂN):**
  ```
  Passed!  - Failed:     0, Passed:   854, Skipped:     0, Total:   854, Duration: 1 s - WorshipPlayer.Tests.dll (net8.0)
  ```
  → **854/854 PASS, 0 fail, 0 skip.** Khớp đúng baseline độc lập của Muse (854/854 tại b245e07). Câu hỏi NETSDK1100 của T1 coi như ĐÃ GIẢI.

### [task T1] Nộp BAO-CAO.md + bằng chứng build/test tại HEAD — 2026-10-22
- Commit đã build (nhánh kiro-current): `b245e0759eac8b1cd39f3afcfbaff9da361f4b82` (ngắn: `b245e07`).
- SDK có sẵn trên môi trường: .NET SDK `8.0.425` và `9.0.318` (solution mặc định dùng `9.0.318`). Môi trường chạy: **Linux sandbox** (app là WPF `net8.0-windows`).
- Đã làm:
  1. Viết `BAO-CAO.md` ở thư mục gốc (nhánh `kiro-current`), đủ 4 phần: (1) phần mềm/công nghệ/tổ chức solution; (2) trạng thái THẬT từng tính năng (lyric presenter + Slides editor ghi chi tiết); (3) cách build + chạy trên Windows từng bước; (4) test tự động + lệnh chạy. Commit `b86df8b` trên `kiro-current`.
  2. Theo lệnh trực tiếp của chủ sản phẩm: **xóa toàn bộ bin/obj**, rồi chạy build toàn solution + `dotnet test`. (Spec T1 gốc ghi "không dọn bin/obj"; lệnh trực tiếp của chủ sản phẩm là trên hết nên đã dọn và ghi rõ ở đây.)

- **Lệnh build đã chạy (toàn solution):**
  `dotnet build WorshipBacktrackPlayer.sln -c Debug --nologo`
- **Kết quả build (NGUYÊN VĂN):**
  ```
  An issue was encountered verifying workloads. For more information, run "dotnet workload update".
    Determining projects to restore...
  /inner_container/opt/toolchains/.local/share/mise/dotnet-root/sdk/9.0.318/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.FrameworkReferenceResolution.targets(107,5): error NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true. [/projects/sandbox/onewcenter/WorshipPlayer.UI/WorshipPlayer.UI.csproj]

  Build FAILED.

  /inner_container/opt/toolchains/.local/share/mise/dotnet-root/sdk/9.0.318/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.FrameworkReferenceResolution.targets(107,5): error NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true. [/projects/sandbox/onewcenter/WorshipPlayer.UI/WorshipPlayer.UI.csproj]
      0 Warning(s)
      1 Error(s)

  Time Elapsed 00:00:00.66
  ```
  → **0 Warning, 1 Error.** Build DỪNG ngay ở project Windows đầu tiên (`WorshipPlayer.UI`, `net8.0-windows`). Không project nào build xong.

- **Lệnh test đã chạy:**
  `dotnet test --nologo`
- **Kết quả test (NGUYÊN VĂN):**
  ```
  An issue was encountered verifying workloads. For more information, run "dotnet workload update".
    Determining projects to restore...
  /inner_container/opt/toolchains/.local/share/mise/dotnet-root/sdk/9.0.318/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.FrameworkReferenceResolution.targets(107,5): error NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true. [/projects/sandbox/onewcenter/WorshipPlayer.UI/WorshipPlayer.UI.csproj]
  ```
  → `dotnet test` (không kèm project) thao tác trên toàn solution nên dừng ở `WorshipPlayer.UI` với cùng lỗi `NETSDK1100`. **KHÔNG test nào được chạy (0 pass / 0 fail) vì build thất bại trước đó.**

- Nguyên nhân: 3 project `WorshipPlayer.UI`, `WorshipPlayer.Presenter`, `WorshipPlayer.Audio` target `net8.0-windows` (WPF). Môi trường Linux không có targeting pack Windows desktop/WPF → lỗi `NETSDK1100`. Đây là **ràng buộc nền tảng**, không phải lỗi code tính năng.
- Theo quy tắc (steering 00/01 + lệnh chủ sản phẩm): **KHÔNG** set `EnableWindowsTargeting=true`, **KHÔNG** sửa .csproj/code để ép build. Ghi nguyên văn lỗi, ghi câu hỏi vào mục CÂU HỎI CHỜ MUSE, và **DỪNG** phần build/test. Việc build/test số liệu pass/fail thật cần chạy trên máy **Windows có .NET 8 SDK**.
- Tham chiếu kỹ thuật: `WorshipPlayer.Tests` (`net8.0`) chỉ có **1 ProjectReference tới `WorshipPlayer.Core`** (không tham chiếu project Windows). Vì vậy trên máy Windows — hoặc khi gọi thẳng `dotnet test WorshipPlayer.Tests/WorshipPlayer.Tests.csproj` — test sẽ chạy được (71 file test, 764 `[Fact]`/`[Theory]`). Trên Linux thì vẫn vướng khâu `dotnet test` toàn solution dừng ở project Windows.

- Tiêu chí nghiệm thu T1:
  - [x] `BAO-CAO.md` tồn tại ở thư mục gốc, đủ 4 phần (đã xong / chưa xong / cách chạy / môi trường). → Commit `b86df8b` nhánh `kiro-current`.
  - [x] PROGRESS.md ghi đúng hash commit đã build (`b245e07`), lệnh đã chạy, kết quả nguyên văn.
  - [ ] Số test trong PROGRESS khớp số test khi Muse đối chiếu: **CHƯA đạt** — không chạy được test trên Linux (lỗi nền tảng `NETSDK1100`). Cần chạy trên Windows để có số pass/fail thật. Xem CÂU HỎI CHỜ MUSE.
- Commit:
  - `b86df8b` [task T1] them BAO-CAO.md ... (nhánh `kiro-current`)
  - (commit PROGRESS.md này — nhánh `main`)

## CÂU HỎI CHỜ MUSE
<!-- Yêu cầu nào mơ hồ/mâu thuẫn thì ghi ở đây, dừng việc liên quan, chờ Muse trả lời qua GitHub. -->

### [ĐÃ GIẢI — task T1] Không build/test được trên môi trường Linux (lỗi NETSDK1100) — 2026-10-22
> **ĐÃ GIẢI (Issue #2):** Muse trả lời — từ nay mọi lệnh build/test trên sandbox Linux phải thêm cờ `-p:EnableWindowsTargeting=true`. Việc 0 đã xác nhận: build sạch 0 Error / 8 Warning, test 854/854 PASS trên Linux. Câu hỏi bên dưới giữ lại để lưu vết.
- Môi trường chạy lệnh là **Linux**, chỉ có .NET SDK (`8.0.425` + `9.0.318`) nhưng **không có targeting pack Windows desktop/WPF**. Ba project `WorshipPlayer.UI`, `WorshipPlayer.Presenter`, `WorshipPlayer.Audio` target `net8.0-windows`.
- `dotnet build` toàn solution và `dotnet test` đều dừng ngay ở `WorshipPlayer.UI` với lỗi:
  `error NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true.`
  → **0 Warning / 1 Error / 0 test chạy.** (Kết quả nguyên văn đã dán ở mục Nhật ký theo task T1.)
- Theo quy tắc: tôi **KHÔNG** set `EnableWindowsTargeting=true` cũng **KHÔNG** sửa .csproj/code để ép build (đó là sửa cấu hình/code ngoài phạm vi T1). Tôi **DỪNG** phần build/test, chờ Muse.
- **Câu hỏi cần Muse quyết:**
  1. Số pass/fail thật của test chỉ có thể lấy trên máy **Windows có .NET 8 SDK**. Muse muốn tôi: (a) chờ chạy lại trên môi trường Windows để nộp số liệu, hay (b) Muse tự chạy `dotnet test` trên máy Windows của dự án để đối chiếu?
  2. Nếu sau này cần build/test trên CI Linux, có cho phép thêm `EnableWindowsTargeting=true` (vào `Directory.Build.props` hoặc .csproj) hay không? Đây là thay đổi cấu hình build (không phải code tính năng) nhưng vẫn ngoài phạm vi T1 nên tôi chờ Muse chốt — chưa tự làm.

## ĐỀ XUẤT (việc ngoài phạm vi Kiro thấy cần — chỉ đề xuất, không tự làm)
<!-- ... -->

### [task T4] Bảng trạng thái thật cho 7 panel/dialog đang "mồ côi" — 2026-10-23 (CHỈ phân tích, CHƯA nối/xóa)
Rà tại HEAD nhánh `kiro-current` (commit `0ddff03`). Căn cứ grep `new <X>` + host trong XAML (bỏ qua các file `obj/*.g.cs` sinh tự động). **3/7 thực ra ĐÃ được dùng**, không còn mồ côi; **4/7 thật sự mồ côi** và đều đã có bản thay thế đang chạy.

| # | Panel/Dialog | Trạng thái thật (bằng chứng file:line) | Đề xuất |
|---|---|---|---|
| 1 | `ServiceGroupPanel` | **MỒ CÔI.** Chỉ xuất hiện trong chính `ServiceGroupPanel.xaml`/`.xaml.cs` (`class ServiceGroupPanel` tại `WorshipPlayer.Presenter/Views/ServiceBuilder/ServiceGroupPanel.xaml.cs:11`). Không có `new ServiceGroupPanel` và không XAML nào host nó. Tính năng "nhóm thứ tự thờ phượng" (ServiceGroupItem/GroupAdded ở `:13,:15`) chưa được nối vào ServiceBuilder. | **(c) Để lại có chủ đích** — là khung tính năng "service groups" chưa tới lượt nối theo roadmap. Giữ, chờ Muse ra task nối vào OperatorWindow ServiceBuilder khi ưu tiên. Không xóa (chưa có bản thay thế). |
| 2 | `BulkEditPanel` | **ĐANG DÙNG — không còn mồ côi.** Được host bởi `BulkEditWindow.xaml:14` (`<local:BulkEditPanel x:Name="BulkEditPanelControl"/>`); `BulkEditWindow` được khởi tạo thật tại `OperatorWindow.xaml.cs:1759` (`new BulkEditWindow(selectedItems.Count){ Owner = this }`). | **(a) Đã nối rồi** qua BulkEditWindow → OperatorWindow bulk-edit. Không cần làm gì. |
| 3 | `ItemSettingsPanel` | **ĐANG DÙNG — không còn mồ côi.** Host bởi `ItemSettingsWindow.xaml:14` (`<local:ItemSettingsPanel x:Name="SettingsPanelControl"/>`); `ItemSettingsWindow` khởi tạo thật tại `OperatorWindow.xaml.cs:1686` (`new ItemSettingsWindow(display.Item, ...Items)`). | **(a) Đã nối rồi** qua ItemSettingsWindow → OperatorWindow item-settings. Không cần làm gì. |
| 4 | `PrintExportDialog` | **ĐANG DÙNG — không còn mồ côi.** Khởi tạo trực tiếp tại `OperatorWindow.xaml.cs:1877` trong handler `OnPrintService` (`:1861`), handler này bind vào menu `MenuPrintService` "Print service" tại `OperatorWindow.xaml:263`. | **(a) Đã nối rồi** vào menu "Print service" của OperatorWindow. Không cần làm gì. |
| 5 | `ImportMediaDialog` | **MỒ CÔI.** Chỉ có trong file của chính nó (`class ImportMediaDialog : Window` tại `ImportMediaDialog.xaml.cs`, không có `new ImportMediaDialog` ở đâu). Luồng import media THẬT dùng `Microsoft.Win32.OpenFileDialog` trong `OnMediaImportRequested` (`OperatorWindow.xaml.cs:3267-3277`) → `ImportMediaFilesAsync`. | **(b) Xóa vì đã có bản thay thế** — OpenFileDialog + ImportMediaFilesAsync đang làm đúng việc import. `ImportMediaDialog` là bản cũ không ai gọi. Đề xuất Muse ra task xóa ở đợt sau (đợt này CHƯA xóa). |
| 6 | `MediaMetadataPanel` | **MỒ CÔI.** Chỉ có trong file của chính nó (`class MediaMetadataPanel : UserControl` tại `MediaMetadataPanel.xaml.cs`). Không `new MediaMetadataPanel`, không XAML host; `MediaLibraryPanel` không tham chiếu metadata panel nào (grep "metadata" trong thư mục MediaLibrary = 0). | **(c) Để lại có chủ đích** — chưa có UI sửa metadata media thay thế (khác với các mục có bản thay thế). Là khung tính năng "sửa metadata media" chưa nối; giữ chờ Muse quyết nối vào MediaLibrary ở đợt sau. Không xóa vì KHÔNG có bản thay thế đang chạy. |
| 7 | `ScriptureSlidePreview` | **MỒ CÔI.** Chỉ có trong file của chính nó (`class ScriptureSlidePreview : UserControl` tại `ScriptureSlidePreview.xaml.cs:11`, `GetLinesPerSlide` cục bộ). Không `new ScriptureSlidePreview`, không XAML host. Luồng Kinh Thánh THẬT dùng `ScriptureBrowserPanel` (khởi tạo tại `OperatorWindow.xaml.cs:2978`), panel này dùng Core `ScriptureSlideGenerator` + preview trong panel (`ScriptureBrowserPanel.xaml.cs:14,:22,:116`). | **(b) Xóa vì đã có bản thay thế** — ScriptureBrowserPanel (dùng Core ScriptureSlideGenerator, "the single splitting implementation") là bản đang chạy; ScriptureSlidePreview là bản preview cũ tự tách dòng, không ai gọi, dễ gây lệch logic tách slide. Đề xuất Muse ra task xóa ở đợt sau (đợt này CHƯA xóa). |

**Tóm tắt cho Muse:** Nối sẵn (không cần làm): #2 BulkEditPanel, #3 ItemSettingsPanel, #4 PrintExportDialog. Đề xuất XÓA (có bản thay thế đang chạy): #5 ImportMediaDialog, #7 ScriptureSlidePreview. Đề xuất GIỮ (khung chưa nối, CHƯA có bản thay thế): #1 ServiceGroupPanel, #6 MediaMetadataPanel. Đúng phạm vi T4: dừng ở bảng, CHƯA nối/xóa gì — chờ Muse duyệt rồi ra task thực thi ở đợt sau.


### [Issue #1] Mo kenh lam viec - push kiro-current - 2026-10-22 12:54
- git push hub HEAD:refs/heads/kiro-current => new branch kiro-current
- git log --oneline -1 => b245e07 fix: reliable touch + mouse input across all three Slides editor modals
- Xac minh GitHub: nhanh kiro-current tai b245e07 (khop local). Pham vi: chi mo kenh, khong sua code.

## CAU HOI CHO MUSE
- Da ton tai remote hub -> owc-multitrack; toi doi ten thanh hub-owcmt roi tao hub -> onewcenter. Nho Muse xac nhan.