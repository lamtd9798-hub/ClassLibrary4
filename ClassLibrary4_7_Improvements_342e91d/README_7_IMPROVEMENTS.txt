CLASSLIBRARY4 - GÓI 7 CẢI TIẾN KIẾN TRÚC
==========================================
Nền đọc từ GitHub: master commit 342e91d578c690bd6813b264b9f68b86621f3b95
Gói này được tạo ngoài GitHub. Không có thao tác ghi ngược lên GitHub.

7 NHÓM ĐƯỢC XỬ LÝ
-----------------
1) MODEL / STATUS / METRICS
   - Giữ đúng model vật lý:
     mep_symbol_classifier.onnx + mep_symbol_labels.txt
     mep_symbol_detector.onnx + mep_symbol_detector_labels.txt
     mep_graph_context.onnx + mep_graph_dn_labels.txt
   - RUNS được nối vào đúng lúc ONNX / YOLO / GNN thật sự gọi _session.Run().
   - Model thiếu không chặn CAD/PIPE/DUCT/GRAPH deterministic.

2) V25 RECOVERY / CLEANUP AN TOÀN
   - Script tự backup source trước khi vá.
   - Không xóa ClassLibrary4.V25*.dll.
   - Không xóa ONNX/OpenCV native runtime.
   - Tùy chọn -CleanupAfterSuccessfulBuild chỉ archive Class1.cs + *.bak sau khi build thành công.

3) EVIDENCE + DECISION + SNAPSHOT + TEST
   - Dùng MepEvidence / MepDecisionService / MepScanSnapshot đã có ở bản mới.
   - Thêm MepDecisionRuntime làm một entry point cho adapter mới.
   - Thêm MepScanSessionStore để giữ snapshot quét hiện tại.
   - Self-test hiện tại được nối thêm runtime integration tests.

4) HAI FINALIZER CŨ / GOD CLASS
   - Gói KHÔNG nhét thêm thuật toán vào BOCTACHUI.xaml.cs.
   - Không thay cả file 80k dòng.
   - Decision entry point mới nằm ngoài god class.
   - Hai finalizer cũ vẫn tương thích; việc di chuyển hoàn toàn body cũ cần kiểm thử CAD regression trước.
     Gói này ưu tiên không phá chức năng đang chạy.

5) PCCC DÙNG SHARED SNAPSHOT
   - Quét AI/HVAC một lần sẽ lưu SelectionSet vào MepScanSessionStore.
   - PCCC đọc diện tích và TEXT/BLOCK ưu tiên dùng snapshot mới; nếu không có/stale thì giữ prompt cũ.
   - Tuyến thủy lực cũng ưu tiên selection snapshot, fallback chọn tay.
   - Chưa tự suy shortest-path bơm -> đầu phun bằng graph nếu graph hiện tại không expose route API;
     không giả lập route sai.

6) HVAC TYPED GRAPH + TAXONOMY
   - Giữ MepGraphDomain Pipe/Duct và typed snapshot đã có.
   - GNN vẫn chỉ là DN evidence cho pipe.
   - Duct/HVAC không bị ép vào GNN DN.
   - Model-specific physical label files vẫn tách riêng, canonical map vẫn dùng MepCanonicalLabelMap.

7) PIPELINE / CLOUD
   - Giữ AiHttpTransport.Shared.
   - Thêm 5 boundary service riêng: LicenseService / SyncService / DatasetService / GraphService / ExportService.
   - Không tạo AiCloudClient god object mới.
   - Có thể migrate client cũ từng service một mà không thay contract một lần.

CÁCH ÁP DỤNG
------------
1. Đóng AutoCAD.
2. Giải nén ZIP.
3. Mở PowerShell tại thư mục repo ClassLibrary4 của anh.
4. Chạy:
   powershell -ExecutionPolicy Bypass -File "<duong_dan>\APPLY_7_IMPROVEMENTS.ps1"

Script sẽ:
- backup các file sắp sửa;
- copy 2 file mới;
- vá ONNX/YOLO/GNN;
- nối shared snapshot cho HVAC/PCCC;
- chạy dotnet restore + build Release x64.

Nếu anh chỉ muốn vá source, chưa build:
   ...\APPLY_7_IMPROVEMENTS.ps1 -SkipBuild

Nếu build thành công và muốn archive Class1.cs + *.bak:
   ...\APPLY_7_IMPROVEMENTS.ps1 -CleanupAfterSuccessfulBuild

KHÔI PHỤC
---------
Script in ra đường dẫn _backup_7_improvements_YYYYMMDD_HHMMSS.
Nếu cần rollback:
   powershell -ExecutionPolicy Bypass -File "<duong_dan>\RESTORE_FROM_BACKUP.ps1" -BackupFolder "<duong_dan_backup>"

LƯU Ý
-----
- Gói này cố ý KHÔNG sửa GitHub.
- Không xóa branch GitHub vì user đã yêu cầu không sửa trực tiếp GitHub.
- Không ghi đè DLL recovery cũ trước khi build/NETLOAD được xác nhận.
