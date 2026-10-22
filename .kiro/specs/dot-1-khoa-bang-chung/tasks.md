# ĐỀ XUẤT SPEC ĐỢT 1 CHO KIRO — "Khóa bằng chứng & nối mắt xích cuối của Slides"

*Do Muse soạn, dựa trên Báo cáo nghiệm thu đợt 1 (nhánh kiro-current @ b245e07). Đợt này KHÔNG làm tính năng mới: mục tiêu là biến mọi "đã xong" hiện có thành "đã xong có bằng chứng", và nối nốt mắt xích còn hở của auto-timed slides. Làm đúng thứ tự T1 → T5; không bắt đầu task sau khi task trước chưa nộp bằng chứng.*

- [x] **T1 — Nộp BAO-CAO.md và bằng chứng build/test tại HEAD hiện tại** (Muse nghiệm thu ĐẠT tại b245e07: build sạch + 854/854 test PASS; Việc 0 xác nhận lại trên Linux với cờ EnableWindowsTargeting)
  - Mục tiêu: Repo có đủ hồ sơ để bất kỳ ai cũng build/chạy/test lại được, và trạng thái HEAD có bằng chứng lệnh thật.
  - Phạm vi LÀM:
    - Viết `BAO-CAO.md` ở thư mục gốc: tính năng đã xong / chưa xong (theo đúng trạng thái thật), cách chạy app từ source (lệnh, thư mục), môi trường cần có (.NET 8 SDK, Windows), ghi chú dữ liệu chạy thật nằm ở đâu (cảnh báo không xóa bin/obj).
    - Chạy tại commit HEAD của nhánh đang làm: `dotnet build WorshipPlayer.UI/WorshipPlayer.UI.csproj -c Debug --nologo` và `dotnet test --nologo`; dán nguyên văn dòng kết quả (số error/warning, số test pass/fail) vào `PROGRESS.md` trên nhánh `main` (theo mẫu có sẵn) và cập nhật luôn mục Trạng thái hiện tại.
  - Phạm vi KHÔNG làm: không sửa code tính năng; không dọn bin/obj; không đổi cấu trúc thư mục.
  - Tiêu chí nghiệm thu:
    - [x] `BAO-CAO.md` tồn tại ở thư mục gốc, đủ 4 phần: đã xong / chưa xong / cách chạy / môi trường.
    - [x] PROGRESS.md ghi đúng hash commit đã build, lệnh đã chạy, kết quả nguyên văn.
    - [x] Số test trong PROGRESS khớp số test khi Muse đối chiếu (Muse: 854; Việc 0 trên Linux: 854).
  - Bằng chứng phải nộp: nội dung BAO-CAO.md + mục PROGRESS.md đã cập nhật (commit + push).

- [ ] **T2 — Kiểm chứng tay cảm ứng + chuột cho 3 modal Slides (theo checklist có sẵn)**
  - Mục tiêu: Chốt tiêu chí hành vi còn mở của commit b245e07: mọi nút trên 3 modal Slides bấm/chạm ăn ngay từ lần đầu khi đang Live.
  - Phạm vi LÀM:
    - Chạy app thật trên máy có màn hình cảm ứng, Go Live (OutputWindow toàn màn hình Topmost), thực hiện đầy đủ checklist trong `.agents/tasks/slides-touch-input/verification.md` (mục "Manual touch test steps") — cả bằng ngón tay và bằng chuột.
    - Ghi kết quả TỪNG mục (đạt / chưa đạt, chạm hay chuột) vào file verification đó (thêm mục kết quả chạy tay có ngày giờ) và tóm tắt vào PROGRESS.md.
    - Nếu có control cụ thể bị "nuốt" thao tác: sửa scoped đúng control đó (theo đúng giới hạn plan đã viết, ví dụ `Stylus.IsPressAndHoldEnabled="False"` tại chỗ), build lại, test lại riêng mục đó.
  - Phạm vi KHÔNG làm: không sửa OperatorWindow PanningMode/IsManipulationEnabled; không thay đổi Topmost của OutputWindow; không mở rộng sang cửa sổ ngoài 3 modal Slides.
  - Tiêu chí nghiệm thu:
    - [ ] Mọi mục trong checklist có kết quả ghi lại, không mục nào để trống/"chưa thử".
    - [ ] Nếu có sửa: commit riêng, build sạch, mục tương ứng đạt khi test lại.
    - [ ] Chuột vẫn hoạt động như trước (không hồi quy bản sửa a0baaed).
  - Bằng chứng phải nộp: verification.md đã bổ sung kết quả chạy tay + PROGRESS.md.

- [x] **T3 — Nối PresenterSettings.LinesPerSlide/SlideLeadMs vào SectionEditorWindow** (commit kiro-current `0ddff03`; build 0 error/8 warning + 856/856 test PASS)
  - Mục tiêu: Cấu hình toàn cục (số dòng/slide + lead ms) trong Settings thật sự đến được pipeline tạo slide, thay vì luôn dùng mặc định 4/500.
  - Phạm vi LÀM:
    - Tại điểm mở `MainWindow.EditSections_Click` (MainWindow.xaml.cs:611): set `LinesPerSlide` và `SlideLeadMs` cho `SectionEditorWindow` từ `PresenterSettings` hiện hành (đọc qua service/settings hiện có, không tạo cơ chế cấu hình mới).
    - Kiểm tra đường đi tiếp: giá trị phải tới được lời gọi Python (`--lines-per-slide`/`--slide-lead-ms`) như spec auto-timed-slides mô tả.
    - Thêm/cập nhật test cho phần ánh xạ giá trị nếu chạm vào code thuần; tối thiểu ghi verification thủ công: đổi Settings → mở editor → số dòng hiệu dụng thay đổi theo.
  - Phạm vi KHÔNG làm: không đổi mặc định 4/500; không sửa UI của Settings; không đụng logic SlideGroupingService/SlideAutoTimingService.
  - Tiêu chí nghiệm thu:
    - [x] Đổi LinesPerSlide trong Settings rồi mở Section Editor: pipeline dùng đúng giá trị mới (có ghi lại cách kiểm chứng).
    - [x] Không set gì trong Settings thì hành vi như cũ (mặc định 4/500).
    - [x] Build sạch + toàn bộ test pass.
  - Bằng chứng phải nộp: commit + mô tả kiểm chứng trong PROGRESS.md (giá trị trước/sau, kết quả build/test nguyên văn).

- [x] **T4 — Bảng trạng thái thật cho 7 panel/dialog đang mồ côi** (bảng 7/7 trong PROGRESS.md mục ĐỀ XUẤT; 3/7 đã dùng, 4/7 mồ côi; chỉ phân tích, chưa nối/xóa)
  - Mục tiêu: Chấm dứt trạng thái mập mờ "có file tưởng đã xong" cho: `ServiceGroupPanel`, `BulkEditPanel`, `ItemSettingsPanel`, `PrintExportDialog`, `ImportMediaDialog`, `MediaMetadataPanel`, `ScriptureSlidePreview`.
  - Phạm vi LÀM:
    - Với từng mục: kiểm tra tại HEAD xem đã có cửa sổ/panel tương đương đang được dùng thật hay chưa (ví dụ BulkEditWindow/ItemSettingsWindow), rồi ghi vào `PROGRESS.md` (mục đề xuất) một bảng 7 dòng: tên — trạng thái thật — đề xuất một trong ba: (a) nối dây vào điểm vào cụ thể nào, (b) xóa vì đã có bản thay thế, (c) để lại có chủ đích kèm lý do.
    - Dừng ở bảng đề xuất — CHƯA nối/xóa gì trong task này; Muse duyệt bảng rồi mới ra task thực thi ở đợt sau.
  - Phạm vi KHÔNG làm: không tự nối dây, không tự xóa file trong task này.
  - Tiêu chí nghiệm thu:
    - [x] Bảng đủ 7/7 mục, mỗi mục có căn cứ file:line hoặc kết quả grep.
    - [x] Không mục nào ghi chung chung kiểu "đang phát triển".
  - Bằng chứng phải nộp: bảng trong PROGRESS.md (commit + push).

- [ ] **T5 — Vệ sinh repo: gỡ file nhạc khỏi git tracking + dọn file patch ở thư mục gốc**
  - Mục tiêu: Repo nhẹ lại và không còn file rác ở gốc, mà không mất bất kỳ file nào trên máy.
  - Phạm vi LÀM:
    - Bỏ track (giữ nguyên file trên đĩa) 25 file .mp3 ở thư mục gốc và trong `Songs/` (`git rm --cached`), bật các dòng loại audio trong `.gitignore` (đang bị comment) — hoặc đề xuất Git LFS trong PROGRESS.md nếu thấy cần giữ lịch sử file nhạc, ghi rõ lựa chọn.
    - Xử lý `latest-changes.patch` ở thư mục gốc: nếu nội dung đã nằm trong code, xóa khỏi repo; nếu cần lưu trữ, chuyển vào `.agents/archive/`.
    - Ghi lại dung lượng repo (.git) trước/sau vào PROGRESS.md.
  - Phạm vi KHÔNG làm: không viết lại lịch sử git (không filter-repo/force-push); không xóa file trên đĩa; không đụng code.
  - Tiêu chí nghiệm thu:
    - [ ] `git ls-files "*.mp3"` trả về 0 (hoặc toàn bộ đã qua LFS theo lựa chọn đã ghi).
    - [ ] Thư mục gốc không còn latest-changes.patch.
    - [ ] App vẫn build/test như trước (kèm kết quả lệnh).
  - Bằng chứng phải nộp: PROGRESS.md (số liệu trước/sau + kết quả build/test) + commit.
