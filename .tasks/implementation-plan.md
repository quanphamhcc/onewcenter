# OWC Presenter — Kế hoạch triển khai (Implementation Plan)

> Lập dựa trên khảo sát thực tế code ngày 2026-10-16. Nguồn dữ liệu: `.agents/tasks/feature-status-map.md`.
> Solution build sạch (net8.0-windows, WPF + ASP.NET Core Kestrel + LibVLCSharp).

## Nguyên tắc xếp thứ tự

1. **Nối trước, xây sau.** Nhiều feature đã có Core service hoàn chỉnh nhưng UI mồ côi (chưa bao giờ `new` trong app). "Quick win" = nối dây, chi phí thấp, giá trị cao → làm trước.
2. **Nền tảng trước lá cây.** Settings (L2–L10) hiện là placeholder tĩnh, không lưu gì. Nhiều feature "đặt mặc định trong settings" phụ thuộc vào cầu nối load/save này → đây là nền tảng, làm sớm.
3. **Dữ liệu trước hiển thị.** Bible DB (E2) chỉ có 4 chương text → scripture gần như rỗng. Nạp dữ liệu là việc tách bạch, giá trị cao.
4. **Việc online/cloud để sau cùng.** C6/C7 (import PCO/ProPresenter), D3 (CCLI SongSelect), M1–M6 (cloud/account), H7 NDI thật, 7.1/7.2 video thật — là việc lớn, net-new, một số cần backend chưa tồn tại.

---

## PHASE 0 — Nền tảng Settings (bắt buộc trước nhiều thứ)

Mục tiêu: biến 10 section Settings từ placeholder tĩnh thành đọc/ghi thật qua `SettingsService` + `AppSettings`.

| Việc | Feature | Ghi chú |
|---|---|---|
| Dựng cầu nối load/save chung cho SettingsWindow | L1 | Mỗi section implement interface Load()/Save() |
| Defaults (fade, aspect ratio, auto-fire, auto-advance) | L2 | Bind control ↔ AppSettings |
| Footer/CCLI (license#, vị trí, font, show-on-slide) | L3 | Model `FooterSettings` đã có; áp footer vào output |
| Interface (editor background, media load) | L4 | Model `InterfaceSettings` đã có |
| Library & Database (media path, reset) | L8 | Nối `MediaLibraryService.GetLibraryPath()` |
| MIDI mapping (6 lệnh → note) | L9 | Model `MidiCommandMapping` + `MidiCueService` đã có |
| Updates & cache (version, cache mgmt) | L10 | Auto-update có thể để sau |

**Vì sao trước:** G7/E5/L5… đều cần "đặt mặc định trong settings". Không có persistence thì các feature kia nối vào hư không.

---

## PHASE 1 — Quick wins: nối UI mồ côi vào Core đã hoàn chỉnh ✅ HOÀN TẤT

Giá trị cao, rủi ro thấp. Tất cả đã merge vào `main` (HEAD `569f33f`). Mỗi feature: build 0 lỗi + test pass + review APPROVED. **Chưa xác nhận UI bằng tay** (app WPF, không chạy headless được).

| Việc | Feature | Trạng thái | Ghi chú kết quả |
|---|---|---|---|
| Template Manager + Editor (3 tab) + built-in | G1, G5, G6 | ✅ DONE | `TemplateService` construct trong PresenterIntegration; Tools→Templates; editor persist qua Create/UpdateAsync; 3 built-in auto-seed |
| ChordPro (.chopro) import | D5 | ✅ DONE | Nút "Import ChordPro…" trong Songs → `ImportChordProAsync` (parser đã có sẵn); lưu `song.json` |
| Song metadata editor | D4 | ✅ DONE | Nút "Edit Metadata…"; thêm field `Song.Ccli`; lưu qua `UpdateMetadataAsync`, giữ nguyên lyrics/sections |
| Song slide WYSIWYG editor | D2 | ✅ DONE | Nút "Edit Slides…"; thêm `Section.BackgroundImage`/`TemplateId` (nullable, backward-compat); lưu giữ chords/timestamps/MIDI/metadata |
| Media filter panel (type/tags) | F3 | ✅ DONE | Dropdown Type/Tag + search + Clear; helper thuần `MediaLibraryFilter` trong Core |
| Per-item auto-advance | C4 | ✅ DONE (Timer); AudioEnd/VideoEnd defer | `ItemSettingsPanel` nối qua sidebar; `AutoAdvanceResolver` thuần + timer trong `PresenterEngine`; mặc định không đổi |

### Việc còn lại / defer trong Phase 1
- **C4 AudioEnd/VideoEnd:** chưa có tín hiệu "media kết thúc" per-item nên hai điều kiện này defer; hiện chỉ có Timer. Hoàn thiện khi có tín hiệu media.
- **Xác nhận UI bằng tay:** chạy app một lượt kiểm tra 6 feature (Tools→Templates; Songs→Import ChordPro / Edit Metadata / Edit Slides; Media filter; service item → Item Settings).

---

## PHASE 2 — Service Builder & tương tác Slide Grid

Việc vừa, chạm model + UI chính của operator window.

| Việc | Feature | Ghi chú |
|---|---|---|
| Nhóm service có thể thu gọn | C2 | UI `ServiceGroupPanel` mồ côi; `ServiceOrderManager` CHƯA có logic group → thêm create/collapse/assign + render trong sidebar |
| Bulk edit (multi-select) | C3 | Thêm multi-select ở sidebar; nối `BulkEditPanel`; batch apply |
| Nền slide hiện trong thumbnail | 11.1 | Vẽ ảnh/màu nền sau chữ trong `RefreshSlideThumbnails` |
| Kéo-thả sắp xếp lại slide | 11.5 | Thêm drag handler + lưu thứ tự |
| Hover: nút sửa/xóa từng slide | 11.5 | Overlay pencil/X |
| Cửa sổ preview pop-out | 11.10 | Thay placeholder `AudiencePreview_Click`/`StagePreview_Click` |
| Gán media làm nền slide | F4 | Drag media → slide; lưu nền |

---

## PHASE 3 — Scripture & Bible data

| Việc | Feature | Ghi chú |
|---|---|---|
| Nạp đủ text KJV (+ WEB) 66 sách | E2 | Hiện ~0.03% câu. Bulk import dữ liệu Kinh Thánh |
| Import Bible từ XML + quản lý bản dịch | L5, E8 | Hiện `ImportXmlButton` chỉ hiện MessageBox; chưa có parser XML |
| Dùng `ScriptureSlideGenerator` cho panel đang chạy | E3 | Core đủ nhưng panel wired tự split riêng → hợp nhất |
| Tùy chọn hiện/ẩn reference & tên bản dịch | E5 | Lộ toggle ra `ScriptureBrowserPanel` + lưu mặc định |
| Template cho scripture | E4 | Phụ thuộc G1/G7 |

---

## PHASE 4 — Việc lớn / net-new (cân nhắc theo nhu cầu)

| Việc | Feature | Độ lớn |
|---|---|---|
| Stage display layout editor (kéo-thả block) | L7, I8 | Lớn, UI mới |
| NDI output thật (SDK NewTek) | H7 | Lớn; hiện là stub bỏ frame |
| Video nền chuyển động thật (VideoView LibVLC) | 7.1 | Hiện render vào ContentControl placeholder |
| Video làm service item | 7.2 | Thêm VideoView + định tuyến item kiểu Video |
| Import Planning Center | C6 | Net-new, cần API client |
| Import ProPresenter | C7 | Net-new, parser file/setlist |
| CCLI SongSelect online import | D3 | Net-new, cần API + key |
| CCLI auto-submit | J4 | Tracking đã xong; auto-submit cần API online |

---

## PHASE 5 — Cloud & Account (Tháng 2–3)

Hầu hết là net-new; một số đã có interface/DTO nhưng chưa có HTTP thật, và chưa có backend.

| Việc | Feature | Trạng thái |
|---|---|---|
| User account menu / login | M1 | MISSING |
| Cloud song library API | M2 | Chỉ có interface + DTO (`Models/Cloud`), chưa có HTTP |
| Desktop sync "Download for Sunday" | M3 | `DesktopSyncService` có state machine nhưng upload/download là stub |
| Team/user management | M4 | MISSING |
| Community marketplace | M5 | MISSING |
| Subscription/billing (Stripe) | M6 | MISSING |
| AirPlay/Miracast | H8 | Chỉ có doc nghiên cứu |

---

## Đã XONG rồi (không cần làm lại)

- Spine chạy service trực tiếp: engine backtrack→slide, output hội chúng.
- Stage display web/iPad cho nhạc công: **H4** per-musician, **I3** capo, **I4** Nashville, **I5** one/two-column — có test.
- **F2** import media cơ bản (file picker) đã nối.
- **J4** usage tracking + report dialog (chỉ auto-submit API là còn thiếu).

---

## Khuyến nghị bắt đầu

1. **Phase 0 (Settings persistence)** — nền tảng, mở khóa nhiều feature khác.
2. Hoặc nếu muốn thấy kết quả nhanh: **Phase 1 — G5/G6/G1 Template** hoặc **D5 ChordPro** (Core đã sẵn, chỉ nối dây).
3. **E2 Bible data** nếu scripture là ưu tiên dùng thực tế mỗi Chủ nhật.
