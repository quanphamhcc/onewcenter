# REQUIREMENTS — Spec đợt 2: "Trình chiếu chuẩn WorshipTools"

*Thư mục spec khi phát hành: `.kiro/specs/dot-2-trinh-chieu-chuan-worshiptools/`. Do Muse soạn dựa trên phân tích WorshipTools (`~/workspace/owc-review/worshiptools/`) và phân tích khoảng trống (`phan-tich-khoang-trong.md` cùng thư mục). Đối chiếu trạng thái thật trong `BAO-CAO.md` (nhánh `kiro-current`) và code tại commit `375b532`.*

## 1. Mục tiêu đợt

Đưa khối Presenter của OneWorshipCenter tiệm cận hành vi của WorshipTools Presenter ở 9 chủ đề đã chốt: lớp nền/nội dung theo item, kế thừa nền, tự chuyển item, headers, nhãn section, thứ tự slide hai cấp, Kinh Thánh dùng được thật, nhiều đầu ra có template riêng, và audio gắn theo item. Đợt này **không** viết lại những phần đã có (auto-advance timer, bulk edit, Slides live-link, groups) — chỉ bổ sung mắt xích còn thiếu theo đúng phân tích khoảng trống.

## 2. Phạm vi

**Trong phạm vi (9 chủ đề R1–R9 bên dưới):** (a) lớp Background + Foreground theo item; (b) Inherit Background + áp qua bulk editor; (c) Auto Advance hoàn chỉnh; (d) Headers; (e) Slide Labels; (f) thứ tự slide cấp bài hát và cấp service item; (g) Quick Scripture, Scripture to Slides, import thêm bản dịch Kinh Thánh từ XML; (h) nhiều outputs + template ghi đè theo output; (i) audio gắn vào service item.

**Ngoài phạm vi (tuyệt đối không làm trong đợt này):**
- SongSelect / CCLI integrations (kết nối tài khoản ngoài, import bài từ SongSelect, auto-reporting mở rộng).
- WorshipTools Cloud sync, tài khoản, phân quyền Organization/Account/User.
- NDI (kể cả alpha) và MIDI in/out cho Presenter — đợt sau; `StubNdiOutput` giữ nguyên.
- Import PowerPoint / Google Slides thành Slides item — đợt sau.
- Nạp sẵn dữ liệu 66 sách cho bất kỳ bản dịch nào (đợt này chỉ làm *cơ chế* import XML).
- Sửa hành vi backtrack/multitrack, section loop theo ms, MaestroDMX.

**Ràng buộc kỹ thuật chung:**
- JSON vẫn là nguồn sự thật; mọi trường mới thêm vào model phải **tương thích ngược**: file service/song/settings cũ thiếu trường vẫn nạp được, giá trị mặc định phải cho hành vi như hiện tại.
- Logic quyết định (resolve, eligibility, thứ tự, ánh xạ) đặt ở `WorshipPlayer.Core` dạng service **thuần** (không WPF) để có unit test — theo đúng tiền lệ `AutoAdvanceResolver`, `SlideGroupingService`.
- Không đổi mặc định hiện hành nào (vd mặc định auto-advance vẫn là Manual) nếu requirement không nói rõ.

---

## R1 — Service Item = Background + Foreground, nền riêng theo từng item *(chủ đề a)*

**User story:** Là người vận hành trình chiếu, tôi muốn mỗi mục trong buổi lễ có lớp nền riêng (hình, video, một màu, hoặc không nền) phủ dưới phần nội dung (lời bài hát / Kinh Thánh / slide), để mỗi phần của buổi lễ có diện mạo riêng mà không phải tạo theme mới.

**Yêu cầu:**
1. Mỗi `ServiceItem` mang cấu hình nền **của riêng nó**, gồm: kiểu nền thuộc một trong {Theo theme (mặc định hiện nay), Không nền (None), Màu đặc (Color), Hình ảnh (Image), Video}; kèm giá trị cụ thể: mã màu khi kiểu Color, đường dẫn file khi kiểu Image/Video.
2. Khi render, lớp nền của item được ghép **dưới** foreground (lyrics/scripture/slides) của chính item đó. Kiểu "Theo theme" giữ nguyên hành vi hiện tại (nền lấy từ `SlideTheme` của item/service).
3. Item được phép **trống hoàn toàn** (không nền tường minh + không foreground) và dùng được như slide chuyển cảnh: tạo được từ Service Builder, chiếu được, điều hướng qua lại được.
4. Nguyên tắc của chuẩn tham chiếu: nền gắn ở **cấp item, không gắn ở cấp bài hát** — không thêm trường nền vào `Song`.

**Trường hợp biên/lỗi:**
- File nền (ảnh/video) bị thiếu/không đọc được khi chiếu → rơi về nền đen, ghi log, **không crash**, foreground vẫn hiển thị.
- File service cũ (chưa có trường nền mới) nạp lên phải cho kết quả hiển thị y hệt trước đợt này.

---

## R2 — Inherit Background và áp dụng qua Bulk Editor *(chủ đề b)*

**User story:** Là người vận hành, tôi muốn nhiều mục liên tiếp (ví dụ nhiều bài hát trên cùng một camera feed hoặc một motion background) dùng chung nền của mục ngay phía trên, để chỉ phải đặt nền một lần và đổi một chỗ là đổi tất cả.

**Yêu cầu:**
1. Mỗi item có thêm lựa chọn nền **Inherit Background**: item dùng nền đã resolve của item **ngay phía trên** nó trong danh sách service.
2. Chuỗi kế thừa: nhiều item liên tiếp cùng kế thừa một nền; item đầu chuỗi phải mang nền thật (không tự nó cũng là inherit). Việc resolve phải đi ngược lên tới item có nền thật hoặc về theme.
3. Item đang ở chế độ inherit hiển thị một dấu hiệu trực quan cạnh tên item trong danh sách service (chuẩn tham chiếu dùng icon mũi tên xuống).
4. Đặt được inherit cho **nhiều item cùng lúc** qua Bulk Editor hiện có: chọn nhiều item → "Set Background" → "Inherit Background" → Apply (mở rộng `BulkEditService`/`BulkEditWindow`, không xây bulk editor mới).

**Trường hợp biên/lỗi:**
- Item đầu tiên của service đặt inherit (không có item phía trên) → resolve về nền theme, không lỗi.
- Item được kế thừa bị xoá hoặc đổi thành inherit tiếp → resolve lại theo chuỗi mới; **không được tạo vòng lặp vô hạn** khi dữ liệu hỏng (hai item trỏ lẫn nhau qua thứ tự — về nguyên tắc inherit chỉ đi lên trên nên vòng lặp không thể xảy ra nếu resolve đúng hướng; test phải chốt điều này).
- Item phía trên đổi nền → các item inherit bên dưới đổi theo ngay khi render kế tiếp (không cache nền đã resolve vào item con).

---

## R3 — Auto Advance hoàn chỉnh theo item *(chủ đề c)*

**User story:** Là người vận hành, tôi muốn các mục không có lời (video mở đầu, slideshow ảnh, thông báo) tự chuyển sang mục kế tiếp sau một số giây hoặc khi video nền chạy hết — kể cả nối thành vòng lặp — để buổi lễ chảy liên tục mà không cần bấm; đồng thời các mục lời bài hát/Kinh Thánh không bao giờ bị tự chuyển ngoài ý muốn.

**Yêu cầu:**
1. **Điều kiện hợp lệ (eligibility) — bắt buộc:** item có foreground là **Lyrics hoặc Scripture thì KHÔNG được auto-advance** dưới mọi cấu hình. Nếu dữ liệu item vừa có cấu hình auto-advance vừa có foreground Lyrics/Scripture, hệ thống phải bỏ qua auto-advance cho item đó (và lý tưởng: UI không cho đặt, hoặc cảnh báo khi đặt). Item foreground Text/Media/Blank và Slides item được phép.
2. **Hai trigger:** (i) sau N giây (đã có — `AutoAdvanceDelayMs` + Timer); (ii) **khi video nền của item chạy hết** (điều kiện `VideoEnd` hiện đang bị resolver bỏ qua dạng "deferred" — phải nối thật: khi item dùng nền Video (R1) hoặc video toàn màn hình và video kết thúc, engine kích hoạt quyết định auto-advance). Nếu item đặt VideoEnd nhưng không có video nào chạy → không tự chuyển (không rơi về Timer).
3. **Điều hướng đích:** Next Item; Specific Item; và vòng lặp: item cuối nối về **First Item** hoặc Specific Item phải tạo được vòng lặp trình chiếu liên tục (cấu hình bằng các đích đã có; nếu cần, bổ sung đích "First Item" tường minh).
4. **Hiển thị đếm ngược:** khi một item đang chạy auto-advance theo Timer, thời gian còn lại hiển thị ở **đầu khung danh sách service** trên cửa sổ Operator (đúng vị trí của chuẩn tham chiếu), cập nhật theo giây, và biến mất/khởi động lại đúng khi chuyển item (kể cả chuyển tay).
5. Item có auto-advance có dấu hiệu trực quan cạnh tên item trong danh sách service (chuẩn tham chiếu dùng icon đồng hồ).
6. Áp auto-advance cho nhiều item qua Bulk Editor: phần này **đã có** (`BulkEditService.ApplyAutoAdvance`) — chỉ cần đảm bảo bulk apply cũng tôn trọng điều kiện hợp lệ ở yêu cầu 1 (item Lyrics/Scripture bị loại khỏi áp dụng hoặc bị engine bỏ qua nhất quán).

**Trường hợp biên/lỗi:**
- Item cuối cùng của service đặt Next Item → không chuyển, không lỗi (hành vi resolver hiện tại — giữ nguyên).
- `GoToItemId` trỏ item đã bị xoá → rơi về Next Item (hành vi hiện tại của resolver — giữ nguyên).
- Người vận hành chuyển item bằng tay trong lúc đang đếm ngược → huỷ đếm ngược của item cũ ngay lập tức.

---

## R4 — Headers (nhóm tiêu đề) trong danh sách service *(chủ đề d)*

**User story:** Là người chuẩn bị buổi lễ, tôi muốn chèn các tiêu đề nhóm ("Mở đầu", "Giảng", "Đáp ứng") vào danh sách để buổi lễ dễ đọc, dễ tìm, mà các tiêu đề này không bao giờ bị chiếu lên màn hình.

**Yêu cầu:**
1. Tạo / đổi tên / xoá / sắp xếp lại header trong Service Builder; gán item vào header; thu gọn/mở rộng (collapse) từng header và trạng thái này được lưu trong file service. *(Hạ tầng đã có: `ServiceOrder.Groups` + `ServiceGroup` + render theo group trong `OperatorWindow` — yêu cầu này là hoàn thiện luồng và chốt bằng chứng, không thiết kế lại model.)*
2. Header **không phải nội dung trình chiếu**: không bao giờ xuất hiện trên output; điều hướng Next/Previous đi qua các item thật (header không phải item nên tiêu chí này phải được kiểm chứng chứ không giả định).
3. Một item thuộc tối đa một header tại một thời điểm; xoá header **không xoá** các item bên trong nó (item trở về không-nhóm hoặc chuyển sang header kề theo lựa chọn mặc định: không-nhóm).
4. Xoá một item khỏi service phải đồng thời gỡ id của nó khỏi danh sách item của header chứa nó (không để id mồ côi — theo tiền lệ `SlideLinkReconciler`).

---

## R5 — Slide Labels cho section bài hát *(chủ đề e)*

**User story:** Là người vận hành, tôi muốn mỗi phần của bài hát (Verse 1, Chorus, Bridge…) có nhãn màu riêng và phím tắt riêng, để nhìn là biết đang ở đâu trong bài và nhảy tới phần cần trong một phím.

**Yêu cầu:**
1. Có **danh mục nhãn dùng chung** (slide label definitions): mỗi nhãn gồm tên, màu, phím tắt (hotkey) — lưu trong `PresenterSettings`. Bộ mặc định theo chuẩn tham chiếu: **Verse 1, Verse 2, Verse 3, Verse 4, Chorus, Bridge** (được phép bổ sung Pre-Chorus, Tag, Intro, Outro nếu giữ đúng 6 nhãn mặc định này).
2. Trang **Settings → Slide Labels**: đổi tên nhãn, tạo nhãn mới, xoá nhãn, đổi màu, gán/đổi phím tắt. Hai nhãn trùng phím tắt → hệ thống cảnh báo và không lưu (hoặc lưu kèm cảnh báo rõ ràng — chốt: **không cho lưu trùng**).
3. Mỗi section của bài hát gán được một nhãn (lưu tham chiếu nhãn trên section; section không gán nhãn vẫn hoạt động như hiện tại). Gán qua dropdown trong trình soạn section/bài hát.
4. Nhãn (tên + màu) hiển thị trong danh sách slide của Operator bên cạnh section tương ứng (`Slide.SectionName` là cầu nối hiện có), giúp định hướng khi chiếu.
5. **Không phá khái niệm section của backtrack**: `Song.Section` hiện gắn với loop points theo ms — nhãn là lớp thông tin *bổ sung* trên section, không đổi tên/ý nghĩa các trường hiện có, không đổi hành vi loop.

**Trường hợp biên/lỗi:**
- Xoá một nhãn đang được section dùng → các section đó trở về "không nhãn", không lỗi nạp file.
- File settings cũ không có danh mục nhãn → tự seed bộ mặc định khi nạp.

---

## R6 — Thứ tự slide hai cấp: Custom Slide Order (cấp bài hát) và Service Item Order (cấp service) *(chủ đề f)*

**User story:** Là người chuẩn bị, tôi muốn bài hát có một thứ tự hát mặc định riêng (ví dụ Verse 1 → Chorus → Verse 2 → Chorus → Bridge → Chorus) áp dụng mỗi lần đưa bài vào buổi lễ; và trong một buổi lễ cụ thể, tôi muốn đổi riêng thứ tự của item đó mà không ảnh hưởng mặc định của bài hát.

**Yêu cầu:**
1. **Custom Slide Order (cấp bài hát):** bài hát lưu được một thứ tự tuỳ biến ở mức section/nhóm-slide: dãy các section **có thể lặp lại** (Chorus xuất hiện nhiều lần), chèn được **slide trống (Blank)** vào bất kỳ vị trí nào. Khi bài hát có custom order, mọi song item tạo mới từ bài hát này dùng custom order làm mặc định. Không đặt custom order → hành vi hiện tại (theo thứ tự section gốc).
2. **Service Item Order (cấp service):** song item trong một service lưu được thứ tự slide **riêng của item đó** (bao gồm nhân đôi một slide và thêm slide), ghi đè custom order của bài hát chỉ trong service hiện tại. Sửa thứ tự ở cấp service **không** làm thay đổi dữ liệu bài hát hay custom order của nó.
3. Thứ tự áp dụng phải đi qua cùng một pipeline sinh slide hiện có (SlideGroupingService/LyricSlideGenerator) — không fork pipeline thứ hai. Độ phân giải thứ tự ưu tiên: Service Item Order (nếu có) → Custom Slide Order (nếu có) → thứ tự section gốc.
4. Việc chia dòng mỗi slide (lines-per-slide) vẫn tôn trọng cấu hình hiện hành (`PresenterSettings.LinesPerSlide` đã nối ở đợt 1 T3) sau khi áp thứ tự mới.

**Trường hợp biên/lỗi:**
- Custom order nhắc tới section đã bị xoá khỏi bài hát → bỏ qua mục đó khi sinh slide, không lỗi.
- Service Item Order chứa id slide không còn tồn tại → bỏ qua id chết; slide mới phát sinh (do sửa lời) được xếp ở cuối theo thứ tự gốc.

---

## R7 — Kinh Thánh: Quick Scripture, Scripture to Slides, import bản dịch từ XML *(chủ đề g)*

**User story:** Là người vận hành, khi mục sư nhắc một câu Kinh Thánh ngoài kế hoạch, tôi muốn gõ tham chiếu là chiếu lên ngay; khi chuẩn bị trước, tôi muốn biến đoạn Kinh Thánh thành bộ slide lưu lại dùng nhiều lần; và tôi muốn nạp thêm bản dịch (ví dụ bản tiếng Việt) từ file XML để dùng như bản dịch có sẵn.

**Yêu cầu:**
1. **Quick Scripture:** từ panel Scripture, gõ/chọn tham chiếu (vd "Giăng 3:16" / "John 3:16" theo parser hiện có `ScriptureReferenceParser`), xem kết quả, và **chiếu ngay lên màn hình khán giả** bằng một thao tác chọn — không bắt buộc phải tạo service item trước. Panel hiện có (`ScriptureBrowserPanel` + event `PresentSlidesRequested`) là điểm bắt đầu: phải kiểm tra và hoàn thiện đường nối từ event này ra output trực tiếp.
2. **Scripture to Slides:** từ cùng panel, tạo một **Slides presentation trong thư viện Slides** chứa các slide của đoạn Kinh Thánh (dùng `ScriptureSlideGenerator` hiện có + hạ tầng live-link `SlidePresentationMapper`), đặt tên được, dùng lại được trong các service sau; thêm được **nhiều đoạn (kể cả không liên tục)** vào cùng một presentation; và chèn presentation đó vào service hiện tại dưới dạng Slides item nếu người dùng chọn.
3. **Import bản dịch từ XML:** trong Settings → Bibles, chọn file `.xml` → hệ thống **parse thật** toàn bộ sách/chương/câu vào cơ sở dữ liệu Kinh Thánh (hiện `BibleDatabaseService.ImportTranslationAsync` chỉ tạo bản ghi từ tên file — phải thay bằng parser thật), đăng ký bản dịch mới (mã, tên, ngôn ngữ lấy từ file nếu có, không có thì suy từ tên file) để xuất hiện trong mọi danh sách chọn bản dịch và tra cứu được ngay bằng `GetVersesAsync`/`SearchAsync`.
   - Định dạng đích của đợt này: **Zefania XML** (định dạng phổ biến tại kho XML công cộng mà tài liệu chuẩn tham chiếu trỏ tới). Các biến thể XML khác: ngoài phạm vi, báo lỗi rõ ràng thay vì import dở.
   - **Muse xác nhận lại trước khi code:** cần một file Zefania XML mẫu thật để làm fixture kiểm chứng trước khi Kiro bắt đầu task parser (T11).
4. Lỗi import (file không tồn tại, sai định dạng, thiếu cấu trúc sách/chương/câu) → thông báo lỗi cụ thể, **không** để lại bản dịch rỗng/nửa vời trong cơ sở dữ liệu (import thất bại thì rollback bản ghi translation).

**Trường hợp biên/lỗi:**
- Quick Scripture với tham chiếu không parse được → báo lỗi tại chỗ, không đổi nội dung đang chiếu.
- Đoạn Kinh Thánh thuộc bản dịch chưa có dữ liệu câu (trạng thái hiện tại của đa số bản dịch) → thông báo "chưa có dữ liệu cho bản dịch này" thay vì chiếu slide trống.

---

## R8 — Nhiều outputs với template ghi đè theo từng output *(chủ đề h)* ⚠

> ⚠ **Mức bằng chứng yếu:** bài docs gốc về Screens & Displays của WorshipTools không được tìm thấy trong lần rà soát; hành vi chuẩn ở chủ đề này tổng hợp từ trang sản phẩm/change log/forum chính thức, và chi tiết "Override Foreground Template" từ guide bên thứ ba. Vì vậy yêu cầu dưới đây được viết **bảo thủ ở mức tối thiểu dùng được**. **Muse xác nhận lại trước khi code** (đối chiếu thêm với trang sản phẩm chính thức) trước khi phát hành task T15/T16.

**User story:** Là người vận hành, tôi muốn màn hình khán giả tại chỗ hiện lời bài hát với template đầy đủ, trong khi đầu ra livestream hiện cùng lời đó dạng lower-third phủ trên hình camera — cùng một nội dung, hai cách trình bày, chạy đồng thời.

**Yêu cầu (tối thiểu):**
1. Cấu hình outputs trở thành **danh sách**: mỗi output gồm tên, chỉ số display đích, loại (Audience/Stage), và **template foreground ghi đè (tuỳ chọn)**. Cấu hình đơn hiện tại (`OutputDisplay`/`StageDisplay`) phải migrate thành danh sách tương đương khi nạp — hành vi với 1 audience output + 1 stage display giữ nguyên như hiện nay.
2. Khi render foreground cho một output, template hiệu dụng được resolve theo thứ tự: **template ghi đè của output** (nếu có) → template của item (nếu có) → template mặc định của service. Background và nội dung (slide nào đang chiếu) giống nhau trên mọi output audience — chỉ *trình bày foreground* khác nhau.
3. Ví dụ bắt buộc đạt: output "Main Audience" không ghi đè → template Default; output "Livestream" ghi đè bằng template Lower Third có sẵn trong `TemplateService` → cùng một song item hiển thị hai kiểu đồng thời trên hai màn hình.
4. Tắt một output trong cấu hình thì cửa sổ output đó đóng/không mở; bật lại thì mở đúng display đã cấu hình. Số output audience đồng thời tối thiểu phải đạt: **2** (tại chỗ + livestream).

**Ngoài phạm vi của R8:** NDI output thật (vẫn stub), layout engine tự do kiểu kéo-thả, alpha channel — đợt sau.

**Trường hợp biên/lỗi:**
- Display đã cấu hình không còn tồn tại khi mở app (rút màn hình) → output đó bị vô hiệu kèm cảnh báo, các output còn lại chạy bình thường (theo tiền lệ `DisplayManager.ClampDisplayIndex`).

---

## R9 — Audio gắn vào service item: nhiều track, loop, phát khi item được kích hoạt *(chủ đề i)*

**User story:** Là người vận hành, tôi muốn gắn sẵn nhạc nền cho mục thông báo (hoặc tiếng đệm cho mục cầu nguyện) để cứ chuyển tới mục đó là nhạc tự phát, và dừng khi chuyển đi.

**Yêu cầu:**
1. Item gắn được **nhiều file audio** (hạ tầng model đã có: `ServiceItem.AudioAttachments` + `ServiceItemAudio.Resolve` — giữ nguyên, không thiết kế lại) và có tuỳ chọn **loop** (lặp lại toàn bộ danh sách khi phát hết) lưu theo item.
2. Khi item được **kích hoạt trình chiếu** (trở thành item hiện tại trên output), audio đính kèm bắt đầu phát; khi rời item (chuyển sang item khác, dừng trình chiếu), audio đính kèm **dừng**. Nhiều track phát **tuần tự** theo thứ tự trong danh sách.
3. Cấu hình được từ UI của item (mục Audio trong cửa sổ/panel cài đặt item hiện có): thêm file từ Media Library hoặc từ máy, xoá file khỏi danh sách, đổi thứ tự, bật/tắt loop.
4. Audio đính kèm **không** xung đột với backtrack: khi item là Song đang dùng backtrack (`UseBacktrack = true`), audio đính kèm không tự phát chồng lên (chỉ phát cho item không dùng backtrack, hoặc khi backtrack của item đó đang không chạy — chốt hành vi đơn giản: **item Song có UseBacktrack = true thì bỏ qua audio đính kèm**).

**Trường hợp biên/lỗi:**
- File audio trong danh sách bị thiếu/không đọc được → bỏ qua file đó, phát file kế tiếp, ghi log, không crash.
- Item không có audio đính kèm → hành vi y hệt hiện tại.

---

## Tiêu chí hoàn thành toàn đợt (Definition of Done — nối tiếp chuẩn đợt 1)

- Mỗi task trong `tasks.md` đạt toàn bộ tiêu chí nghiệm thu của task đó.
- Build toàn solution sạch (0 error) với cờ `-p:EnableWindowsTargeting=true`; toàn bộ test cũ vẫn pass (đường cơ sở khi viết spec: **856 test**) cộng test mới của đợt.
- Mọi thay đổi model đều nạp được file dữ liệu cũ (service/song/settings/Bible DB hiện hữu) — có test tương thích ngược cho từng model chạm vào.
- Bằng chứng nộp trong `PROGRESS.md` theo đúng quy ước đợt 1 (lệnh đã chạy + kết quả nguyên văn + hash commit).
