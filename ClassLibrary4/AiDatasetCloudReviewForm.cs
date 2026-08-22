#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassLibrary4
{
    public sealed class AiDatasetCloudReviewForm : Form
    {
        private readonly AiCloudConfig _config;
        private readonly AiDatasetCloudAdminClient _client;

        private readonly ComboBox _filterBox = new ComboBox();
        private readonly Label _summaryLabel = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private readonly PictureBox _preview = new PictureBox();
        private readonly Label _detailLabel = new Label();
        private readonly Button _refreshButton = new Button();
        private readonly Button _approveButton = new Button();
        private readonly Button _rejectButton = new Button();
        private readonly Button _classButton = new Button();
        private readonly Button _closeButton = new Button();

        private readonly List<Bitmap> _ownedBitmaps = new List<Bitmap>();
        private List<AiDatasetCloudReviewRow> _rows =
            new List<AiDatasetCloudReviewRow>();
        private List<AiClassDictionaryItem> _classes =
            new List<AiClassDictionaryItem>();

        private string _adminKey = "";
        private bool _loading = false;

        public AiDatasetCloudReviewForm(
            AiCloudConfig config,
            AiDatasetCloudAdminClient client)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            Text = "AI DATASET CLOUD - DUYỆT MẪU / CLASS DICTIONARY";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 1500;
            Height = 860;
            MinimumSize = new Size(1200, 720);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            BuildUi();

            Shown += async (s, e) =>
            {
                await RefreshAllAsync();
            };

            FormClosed += (s, e) =>
            {
                DisposeOwnedBitmaps();
            };
        }

        private void BuildUi()
        {
            Label title = new Label
            {
                Left = 18,
                Top = 14,
                Width = 900,
                Height = 30,
                Text = "AI DATASET CLOUD - REVIEW / APPROVE / CLASS DICTIONARY",
                Font = new Font("Segoe UI", 12.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(76, 29, 149)
            };

            _summaryLabel.Left = 18;
            _summaryLabel.Top = 47;
            _summaryLabel.Width = 930;
            _summaryLabel.Height = 24;
            _summaryLabel.Text = "Đang tải dữ liệu Cloud...";
            _summaryLabel.ForeColor = Color.FromArgb(71, 85, 105);

            Label filterLabel = new Label
            {
                Left = 970,
                Top = 22,
                Width = 85,
                Height = 24,
                Text = "TRẠNG THÁI:"
            };

            _filterBox.Left = 1060;
            _filterBox.Top = 18;
            _filterBox.Width = 190;
            _filterBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _filterBox.Items.AddRange(
                new object[]
                {
                    "TẤT CẢ",
                    "APPROVED",
                    "PENDING",
                    "CONFLICT",
                    "REJECTED"
                });
            _filterBox.SelectedIndex = 0;
            _filterBox.SelectedIndexChanged += async (s, e) =>
            {
                if (Visible)
                    await RefreshRowsAsync();
            };

            _refreshButton.Left = 1265;
            _refreshButton.Top = 16;
            _refreshButton.Width = 105;
            _refreshButton.Height = 32;
            _refreshButton.Text = "LÀM MỚI";
            _refreshButton.Click += async (s, e) => await RefreshAllAsync();

            _classButton.Left = 1380;
            _classButton.Top = 16;
            _classButton.Width = 95;
            _classButton.Height = 32;
            _classButton.Text = "TỪ ĐIỂN";
            _classButton.BackColor = Color.FromArgb(245, 243, 255);
            _classButton.ForeColor = Color.FromArgb(91, 33, 182);
            _classButton.FlatStyle = FlatStyle.Flat;
            _classButton.Click += async (s, e) => await ShowClassDictionaryDialogAsync();

            _grid.Left = 18;
            _grid.Top = 82;
            _grid.Width = 1120;
            _grid.Height = 690;
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = Color.White;
            _grid.RowTemplate.Height = 84;
            _grid.ColumnHeadersHeight = 40;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.SelectionChanged += (s, e) => UpdateSelectedPreview();
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    ShowLargePreview();
            };

            DataGridViewImageColumn imageColumn = new DataGridViewImageColumn
            {
                Name = "IMAGE",
                HeaderText = "KÝ HIỆU",
                Width = 100,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            _grid.Columns.Add(imageColumn);

            AddTextColumn("STATUS", "TRẠNG THÁI", 125);
            AddTextColumn("CLASS", "CLASS", 125);
            AddTextColumn("WINNER", "NHÃN ĐANG THẮNG", 250);
            AddTextColumn("VOTES", "VOTE", 85);
            AddTextColumn("SECOND", "NHÃN THỨ 2", 200);
            AddTextColumn("NEGATIVE", "NEG", 60);
            AddTextColumn("HARDNEG", "HARD NEGATIVE", 190);
            AddTextColumn("MODE", "NGUỒN", 90);

            Panel rightPanel = new Panel
            {
                Left = 1155,
                Top = 82,
                Width = 320,
                Height = 690,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label previewTitle = new Label
            {
                Left = 14,
                Top = 12,
                Width = 280,
                Height = 24,
                Text = "MẪU ĐANG CHỌN",
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };

            _preview.Left = 14;
            _preview.Top = 42;
            _preview.Width = 290;
            _preview.Height = 250;
            _preview.BackColor = Color.FromArgb(30, 41, 59);
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.DoubleClick += (s, e) => ShowLargePreview();

            _detailLabel.Left = 14;
            _detailLabel.Top = 305;
            _detailLabel.Width = 290;
            _detailLabel.Height = 205;
            _detailLabel.Text = "Chọn một mẫu trong bảng.";
            _detailLabel.ForeColor = Color.FromArgb(51, 65, 85);

            _approveButton.Left = 14;
            _approveButton.Top = 525;
            _approveButton.Width = 290;
            _approveButton.Height = 42;
            _approveButton.Text = "DUYỆT / GÁN CLASS";
            _approveButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _approveButton.BackColor = Color.FromArgb(220, 252, 231);
            _approveButton.ForeColor = Color.FromArgb(22, 101, 52);
            _approveButton.FlatStyle = FlatStyle.Flat;
            _approveButton.Click += async (s, e) => await ApproveSelectedAsync();

            _rejectButton.Left = 14;
            _rejectButton.Top = 575;
            _rejectButton.Width = 290;
            _rejectButton.Height = 42;
            _rejectButton.Text = "LOẠI MẪU KHỎI DATASET";
            _rejectButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _rejectButton.BackColor = Color.FromArgb(254, 226, 226);
            _rejectButton.ForeColor = Color.FromArgb(185, 28, 28);
            _rejectButton.FlatStyle = FlatStyle.Flat;
            _rejectButton.Click += async (s, e) => await RejectSelectedAsync();

            Label note = new Label
            {
                Left = 14,
                Top = 630,
                Width = 290,
                Height = 50,
                Text = "Admin duyệt sẽ ghi đè consensus. Admin Key chỉ giữ trong RAM của phiên này, không lưu vào file cấu hình.",
                ForeColor = Color.FromArgb(100, 116, 139)
            };

            rightPanel.Controls.Add(previewTitle);
            rightPanel.Controls.Add(_preview);
            rightPanel.Controls.Add(_detailLabel);
            rightPanel.Controls.Add(_approveButton);
            rightPanel.Controls.Add(_rejectButton);
            rightPanel.Controls.Add(note);

            _closeButton.Text = "ĐÓNG";
            _closeButton.Width = 120;
            _closeButton.Height = 36;
            _closeButton.Left = 1355;
            _closeButton.Top = 784;
            _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _closeButton.DialogResult = DialogResult.OK;

            Controls.Add(title);
            Controls.Add(_summaryLabel);
            Controls.Add(filterLabel);
            Controls.Add(_filterBox);
            Controls.Add(_refreshButton);
            Controls.Add(_classButton);
            Controls.Add(_grid);
            Controls.Add(rightPanel);
            Controls.Add(_closeButton);

            AcceptButton = _closeButton;
        }

        private void AddTextColumn(
            string name,
            string header,
            int width)
        {
            DataGridViewTextBoxColumn col =
                new DataGridViewTextBoxColumn
                {
                    Name = name,
                    HeaderText = header,
                    Width = width
                };

            _grid.Columns.Add(col);
        }

        private string CurrentFilter
        {
            get
            {
                switch (_filterBox.SelectedIndex)
                {
                    case 1:
                        return "APPROVED";
                    case 2:
                        return "PENDING";
                    case 3:
                        return "CONFLICT";
                    case 4:
                        return "REJECTED";
                    default:
                        return "ALL";
                }
            }
        }

        private async Task RefreshAllAsync()
        {
            if (_loading)
                return;

            _loading = true;
            SetBusy(true, "Đang tải Cloud Dataset...");

            try
            {
                _classes =
                    await _client.GetClassesAsync(
                        _config);

                await RefreshRowsAsyncCore();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không tải được Cloud Dataset:\n\n" + ex.Message,
                    "AI DATASET CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _loading = false;
                SetBusy(false, null);
            }
        }

        private async Task RefreshRowsAsync()
        {
            if (_loading)
                return;

            _loading = true;
            SetBusy(true, "Đang lọc Dataset...");

            try
            {
                await RefreshRowsAsyncCore();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không tải được Cloud Dataset:\n\n" + ex.Message,
                    "AI DATASET CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _loading = false;
                SetBusy(false, null);
            }
        }

        private async Task RefreshRowsAsyncCore()
        {
            DisposeOwnedBitmaps();
            _grid.Rows.Clear();
            _preview.Image = null;
            _detailLabel.Text = "Chọn một mẫu trong bảng.";

            _rows =
                await _client.GetReviewRowsAsync(
                    _config,
                    CurrentFilter,
                    300);

            int approved = 0;
            int pending = 0;
            int conflict = 0;
            int rejected = 0;

            foreach (AiDatasetCloudReviewRow row in _rows)
            {
                if (row == null)
                    continue;

                if (row.Status.StartsWith("APPROVED", StringComparison.OrdinalIgnoreCase))
                    approved++;
                else if (row.Status.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase))
                    rejected++;
                else if (string.Equals(row.Status, "CONFLICT", StringComparison.OrdinalIgnoreCase))
                    conflict++;
                else
                    pending++;

                Bitmap thumb = null;

                try
                {
                    thumb =
                        await _client.DownloadPreviewAsync(
                            row.SignedUrl);

                    if (thumb != null)
                        _ownedBitmaps.Add(thumb);
                }
                catch
                {
                    thumb = null;
                }

                int gridRowIndex =
                    _grid.Rows.Add(
                        thumb,
                        row.Status,
                        string.IsNullOrWhiteSpace(row.ClassCode)
                            ? "-"
                            : row.ClassCode,
                        string.IsNullOrWhiteSpace(row.FinalLabel)
                            ? row.WinnerLabel
                            : row.FinalLabel,
                        row.WinnerVotes + "/" + row.VoterCount,
                        string.IsNullOrWhiteSpace(row.SecondLabel)
                            ? "-"
                            : row.SecondLabel + " (" + row.SecondVotes + ")",
                        row.NegativeVotes,
                        string.IsNullOrWhiteSpace(row.HardNegativeLabel)
                            ? "-"
                            : row.HardNegativeLabel,
                        row.MatchMode);

                DataGridViewRow gridRow = _grid.Rows[gridRowIndex];
                gridRow.Tag = row;

                ApplyRowColor(gridRow, row.Status);
            }

            _summaryLabel.Text =
                "Hiển thị " + _rows.Count +
                " mẫu  |  Approved " + approved +
                "  |  Pending " + pending +
                "  |  Conflict " + conflict +
                "  |  Rejected " + rejected +
                "  |  Class Dictionary " + _classes.Count;

            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected = true;
                _grid.CurrentCell = _grid.Rows[0].Cells[0];
                UpdateSelectedPreview();
            }
        }

        private void ApplyRowColor(
            DataGridViewRow row,
            string status)
        {
            if (row == null)
                return;

            if (status.StartsWith("APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 244);
            }
            else if (status.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(254, 242, 242);
            }
            else if (string.Equals(status, "CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 247, 237);
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            }
        }

        private AiDatasetCloudReviewRow GetSelectedRow()
        {
            if (_grid.SelectedRows.Count == 0)
                return null;

            return _grid.SelectedRows[0].Tag as AiDatasetCloudReviewRow;
        }

        private void UpdateSelectedPreview()
        {
            AiDatasetCloudReviewRow row = GetSelectedRow();

            if (row == null)
            {
                _preview.Image = null;
                _detailLabel.Text = "Chọn một mẫu trong bảng.";
                return;
            }

            _preview.Image =
                _grid.SelectedRows.Count > 0
                    ? _grid.SelectedRows[0].Cells["IMAGE"].Value as Image
                    : null;

            string displayLabel =
                string.IsNullOrWhiteSpace(row.FinalLabel)
                    ? row.WinnerLabel
                    : row.FinalLabel;

            _detailLabel.Text =
                "TRẠNG THÁI: " + row.Status + "\r\n" +
                "TÊN: " + (displayLabel ?? "") + "\r\n" +
                "CLASS: " + (string.IsNullOrWhiteSpace(row.ClassCode) ? "-" : row.ClassCode) + "\r\n" +
                "VOTE: " + row.WinnerVotes + "/" + row.VoterCount + "\r\n" +
                "NHÃN 2: " + (string.IsNullOrWhiteSpace(row.SecondLabel) ? "-" : row.SecondLabel) +
                    " (" + row.SecondVotes + ")\r\n" +
                "NEGATIVE: " + row.NegativeVotes + "\r\n" +
                "THEO DN: " + (row.FollowDn ? "CÓ" : "KHÔNG") + "\r\n" +
                "HASH: " + ShortHash(row.SampleHash);
        }

        private void ShowLargePreview()
        {
            Image image = _preview.Image;

            if (image == null)
                return;

            using (Form form = new Form())
            using (PictureBox box = new PictureBox())
            {
                form.Text = "PREVIEW KÝ HIỆU";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Width = 650;
                form.Height = 650;
                form.BackColor = Color.FromArgb(15, 23, 42);

                box.Dock = DockStyle.Fill;
                box.SizeMode = PictureBoxSizeMode.Zoom;
                box.Image = image;

                form.Controls.Add(box);
                form.ShowDialog(this);
            }
        }

        private async Task ApproveSelectedAsync()
        {
            AiDatasetCloudReviewRow row = GetSelectedRow();
            if (row == null)
                return;

            string adminKey = PromptAdminKey();
            if (string.IsNullOrWhiteSpace(adminKey))
                return;

            if (!ShowApproveDialog(
                    row,
                    out string finalLabel,
                    out string classCode,
                    out bool followDn,
                    out string note))
            {
                return;
            }

            try
            {
                _approveButton.Enabled = false;
                _approveButton.Text = "ĐANG DUYỆT...";

                AiClassDictionaryItem existingClass =
                    _classes.FirstOrDefault(
                        x =>
                            string.Equals(
                                x.ClassCode,
                                classCode,
                                StringComparison.OrdinalIgnoreCase));

                if (existingClass == null)
                {
                    AiDatasetAdminActionResult classResult =
                        await _client.UpsertClassAsync(
                            _config,
                            adminKey,
                            classCode,
                            finalLabel,
                            new string[] { finalLabel },
                            true);

                    if (!classResult.Ok)
                    {
                        MessageBox.Show(
                            "Không tạo được CLASS:\n" + classResult.Error,
                            "AI DATASET CLOUD",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                AiDatasetAdminActionResult result =
                    await _client.ApproveAsync(
                        _config,
                        adminKey,
                        row.SampleHash,
                        finalLabel,
                        classCode,
                        followDn,
                        note,
                        _config.VoterId);

                if (!result.Ok)
                {
                    MessageBox.Show(
                        "Duyệt mẫu thất bại:\n" + result.Error,
                        "AI DATASET CLOUD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                await RefreshAllAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Duyệt mẫu thất bại:\n" + ex.Message,
                    "AI DATASET CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _approveButton.Enabled = true;
                _approveButton.Text = "DUYỆT / GÁN CLASS";
            }
        }

        private async Task RejectSelectedAsync()
        {
            AiDatasetCloudReviewRow row = GetSelectedRow();
            if (row == null)
                return;

            DialogResult confirm = MessageBox.Show(
                "Loại mẫu này khỏi Dataset dùng để train?\n\n" +
                "Mẫu vẫn được giữ trong Storage để audit, nhưng trạng thái sẽ là REJECTED_ADMIN.",
                "AI DATASET CLOUD",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            string adminKey = PromptAdminKey();
            if (string.IsNullOrWhiteSpace(adminKey))
                return;

            string note = PromptText(
                "LÝ DO LOẠI MẪU",
                "Ghi chú (có thể để trống):",
                row.ReviewNote ?? "",
                false);

            if (note == null)
                return;

            try
            {
                _rejectButton.Enabled = false;
                _rejectButton.Text = "ĐANG LOẠI...";

                AiDatasetAdminActionResult result =
                    await _client.RejectAsync(
                        _config,
                        adminKey,
                        row.SampleHash,
                        note,
                        _config.VoterId);

                if (!result.Ok)
                {
                    MessageBox.Show(
                        "Loại mẫu thất bại:\n" + result.Error,
                        "AI DATASET CLOUD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                await RefreshRowsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Loại mẫu thất bại:\n" + ex.Message,
                    "AI DATASET CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _rejectButton.Enabled = true;
                _rejectButton.Text = "LOẠI MẪU KHỎI DATASET";
            }
        }

        private bool ShowApproveDialog(
            AiDatasetCloudReviewRow row,
            out string finalLabel,
            out string classCode,
            out bool followDn,
            out string note)
        {
            finalLabel = "";
            classCode = "";
            followDn = false;
            note = "";

            using (Form form = new Form())
            using (Label label1 = new Label())
            using (TextBox labelBox = new TextBox())
            using (Label label2 = new Label())
            using (ComboBox classBox = new ComboBox())
            using (CheckBox followBox = new CheckBox())
            using (Label label3 = new Label())
            using (TextBox noteBox = new TextBox())
            using (Button okButton = new Button())
            using (Button cancelButton = new Button())
            {
                form.Text = "DUYỆT MẪU AI";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Width = 760;
                form.Height = 400;
                form.BackColor = Color.White;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                label1.Left = 24;
                label1.Top = 28;
                label1.Width = 180;
                label1.Text = "TÊN THIẾT BỊ CHUẨN:";

                labelBox.Left = 220;
                labelBox.Top = 24;
                labelBox.Width = 500;
                labelBox.Text =
                    !string.IsNullOrWhiteSpace(row.FinalLabel)
                        ? row.FinalLabel
                        : row.WinnerLabel;

                label2.Left = 24;
                label2.Top = 78;
                label2.Width = 180;
                label2.Text = "CLASS CODE:";

                classBox.Left = 220;
                classBox.Top = 74;
                classBox.Width = 500;
                classBox.DropDownStyle = ComboBoxStyle.DropDown;

                foreach (AiClassDictionaryItem item in _classes
                    .Where(x => x != null && x.Active)
                    .OrderBy(x => x.ClassCode))
                {
                    classBox.Items.Add(item.ClassCode);
                }

                classBox.Text =
                    !string.IsNullOrWhiteSpace(row.ClassCode)
                        ? row.ClassCode
                        : BuildClassCode(labelBox.Text);

                labelBox.TextChanged += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(classBox.Text) ||
                        classBox.Text == BuildClassCode(row.WinnerLabel))
                    {
                        classBox.Text = BuildClassCode(labelBox.Text);
                    }
                };

                followBox.Left = 220;
                followBox.Top = 120;
                followBox.Width = 280;
                followBox.Text = "LẤY SIZE THEO DN ỐNG";
                followBox.Checked = row.FollowDn;

                label3.Left = 24;
                label3.Top = 165;
                label3.Width = 180;
                label3.Text = "GHI CHÚ DUYỆT:";

                noteBox.Left = 220;
                noteBox.Top = 160;
                noteBox.Width = 500;
                noteBox.Height = 90;
                noteBox.Multiline = true;
                noteBox.Text = row.ReviewNote ?? "";

                Label consensus = new Label
                {
                    Left = 24,
                    Top = 270,
                    Width = 500,
                    Height = 45,
                    Text =
                        "Consensus: " + row.WinnerLabel + " = " + row.WinnerVotes +
                        " vote | " +
                        (string.IsNullOrWhiteSpace(row.SecondLabel)
                            ? "không có nhãn thứ 2"
                            : row.SecondLabel + " = " + row.SecondVotes) +
                        " | Negative = " + row.NegativeVotes,
                    ForeColor = Color.FromArgb(71, 85, 105)
                };

                okButton.Left = 485;
                okButton.Top = 310;
                okButton.Width = 120;
                okButton.Height = 38;
                okButton.Text = "DUYỆT";
                okButton.BackColor = Color.FromArgb(34, 197, 94);
                okButton.ForeColor = Color.White;
                okButton.FlatStyle = FlatStyle.Flat;
                okButton.DialogResult = DialogResult.OK;

                cancelButton.Left = 615;
                cancelButton.Top = 310;
                cancelButton.Width = 105;
                cancelButton.Height = 38;
                cancelButton.Text = "HỦY";
                cancelButton.DialogResult = DialogResult.Cancel;

                form.Controls.Add(label1);
                form.Controls.Add(labelBox);
                form.Controls.Add(label2);
                form.Controls.Add(classBox);
                form.Controls.Add(followBox);
                form.Controls.Add(label3);
                form.Controls.Add(noteBox);
                form.Controls.Add(consensus);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);

                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog(this) != DialogResult.OK)
                    return false;

                finalLabel = (labelBox.Text ?? "").Trim();
                classCode = NormalizeClassCode(classBox.Text);
                followDn = followBox.Checked;
                note = (noteBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(finalLabel) ||
                    string.IsNullOrWhiteSpace(classCode))
                {
                    MessageBox.Show(
                        "Tên thiết bị và CLASS CODE không được để trống.",
                        "AI DATASET CLOUD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                return true;
            }
        }

        private async Task ShowClassDictionaryDialogAsync()
        {
            try
            {
                _classes = await _client.GetClassesAsync(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không tải được Class Dictionary:\n" + ex.Message,
                    "AI DATASET CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (Form form = new Form())
            using (DataGridView grid = new DataGridView())
            using (Button addButton = new Button())
            using (Button editButton = new Button())
            using (Button closeButton = new Button())
            {
                form.Text = "CLASS DICTIONARY - TÊN CHUẨN CHO ONNX";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Width = 1050;
                form.Height = 650;
                form.BackColor = Color.White;

                grid.Left = 18;
                grid.Top = 18;
                grid.Width = 995;
                grid.Height = 520;
                grid.AllowUserToAddRows = false;
                grid.ReadOnly = true;
                grid.MultiSelect = false;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.RowHeadersVisible = false;
                grid.AutoGenerateColumns = false;
                grid.BackgroundColor = Color.White;

                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "CODE",
                    HeaderText = "CLASS CODE",
                    Width = 210
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "NAME",
                    HeaderText = "TÊN CHUẨN",
                    Width = 360
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "ALIASES",
                    HeaderText = "TÊN ĐỒNG NGHĨA",
                    Width = 320
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "COUNT",
                    HeaderText = "MẪU",
                    Width = 70
                });

                Action reloadGrid = () =>
                {
                    grid.Rows.Clear();
                    foreach (AiClassDictionaryItem item in _classes
                        .OrderBy(x => x.ClassCode))
                    {
                        int index = grid.Rows.Add(
                            item.ClassCode,
                            item.DisplayName,
                            string.Join(", ", item.Aliases ?? new List<string>()),
                            item.SampleCount);
                        grid.Rows[index].Tag = item;
                    }
                };

                reloadGrid();

                addButton.Left = 18;
                addButton.Top = 555;
                addButton.Width = 170;
                addButton.Height = 38;
                addButton.Text = "+ THÊM CLASS";
                addButton.BackColor = Color.FromArgb(237, 233, 254);
                addButton.ForeColor = Color.FromArgb(91, 33, 182);
                addButton.FlatStyle = FlatStyle.Flat;
                addButton.Click += async (s, e) =>
                {
                    AiClassDictionaryItem item = new AiClassDictionaryItem();
                    if (!ShowClassEditDialog(item, true))
                        return;

                    string adminKey = PromptAdminKey();
                    if (string.IsNullOrWhiteSpace(adminKey))
                        return;

                    AiDatasetAdminActionResult result =
                        await _client.UpsertClassAsync(
                            _config,
                            adminKey,
                            item.ClassCode,
                            item.DisplayName,
                            item.Aliases,
                            item.Active);

                    if (!result.Ok)
                    {
                        MessageBox.Show(result.Error, "CLASS DICTIONARY");
                        return;
                    }

                    _classes = await _client.GetClassesAsync(_config);
                    reloadGrid();
                };

                editButton.Left = 198;
                editButton.Top = 555;
                editButton.Width = 170;
                editButton.Height = 38;
                editButton.Text = "SỬA CLASS";
                editButton.Click += async (s, e) =>
                {
                    if (grid.SelectedRows.Count == 0)
                        return;

                    AiClassDictionaryItem source =
                        grid.SelectedRows[0].Tag as AiClassDictionaryItem;

                    if (source == null)
                        return;

                    AiClassDictionaryItem edit = new AiClassDictionaryItem
                    {
                        ClassCode = source.ClassCode,
                        DisplayName = source.DisplayName,
                        Aliases = new List<string>(source.Aliases ?? new List<string>()),
                        Active = source.Active
                    };

                    if (!ShowClassEditDialog(edit, false))
                        return;

                    string adminKey = PromptAdminKey();
                    if (string.IsNullOrWhiteSpace(adminKey))
                        return;

                    AiDatasetAdminActionResult result =
                        await _client.UpsertClassAsync(
                            _config,
                            adminKey,
                            edit.ClassCode,
                            edit.DisplayName,
                            edit.Aliases,
                            edit.Active);

                    if (!result.Ok)
                    {
                        MessageBox.Show(result.Error, "CLASS DICTIONARY");
                        return;
                    }

                    _classes = await _client.GetClassesAsync(_config);
                    reloadGrid();
                };

                closeButton.Left = 845;
                closeButton.Top = 555;
                closeButton.Width = 168;
                closeButton.Height = 38;
                closeButton.Text = "ĐÓNG";
                closeButton.DialogResult = DialogResult.OK;

                form.Controls.Add(grid);
                form.Controls.Add(addButton);
                form.Controls.Add(editButton);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;

                form.ShowDialog(this);
            }
        }

        private bool ShowClassEditDialog(
            AiClassDictionaryItem item,
            bool isNew)
        {
            using (Form form = new Form())
            using (Label l1 = new Label())
            using (TextBox codeBox = new TextBox())
            using (Label l2 = new Label())
            using (TextBox nameBox = new TextBox())
            using (Label l3 = new Label())
            using (TextBox aliasesBox = new TextBox())
            using (CheckBox activeBox = new CheckBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                form.Text = isNew ? "THÊM CLASS" : "SỬA CLASS";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Width = 720;
                form.Height = 380;
                form.BackColor = Color.White;

                l1.Left = 24; l1.Top = 30; l1.Width = 160; l1.Text = "CLASS CODE:";
                codeBox.Left = 200; codeBox.Top = 26; codeBox.Width = 470;
                codeBox.Text = item.ClassCode ?? "";
                codeBox.ReadOnly = !isNew;

                l2.Left = 24; l2.Top = 80; l2.Width = 160; l2.Text = "TÊN CHUẨN:";
                nameBox.Left = 200; nameBox.Top = 76; nameBox.Width = 470;
                nameBox.Text = item.DisplayName ?? "";

                l3.Left = 24; l3.Top = 130; l3.Width = 160; l3.Text = "TÊN ĐỒNG NGHĨA:";
                aliasesBox.Left = 200; aliasesBox.Top = 126; aliasesBox.Width = 470; aliasesBox.Height = 95;
                aliasesBox.Multiline = true;
                aliasesBox.Text = string.Join(", ", item.Aliases ?? new List<string>());

                activeBox.Left = 200; activeBox.Top = 235; activeBox.Width = 200;
                activeBox.Text = "CLASS ĐANG HOẠT ĐỘNG";
                activeBox.Checked = item.Active;

                if (isNew)
                {
                    nameBox.TextChanged += (s, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(codeBox.Text))
                            codeBox.Text = BuildClassCode(nameBox.Text);
                    };
                }

                ok.Left = 435; ok.Top = 285; ok.Width = 110; ok.Height = 38;
                ok.Text = "LƯU"; ok.DialogResult = DialogResult.OK;
                ok.BackColor = Color.FromArgb(124, 58, 237); ok.ForeColor = Color.White; ok.FlatStyle = FlatStyle.Flat;

                cancel.Left = 560; cancel.Top = 285; cancel.Width = 110; cancel.Height = 38;
                cancel.Text = "HỦY"; cancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(l1); form.Controls.Add(codeBox);
                form.Controls.Add(l2); form.Controls.Add(nameBox);
                form.Controls.Add(l3); form.Controls.Add(aliasesBox);
                form.Controls.Add(activeBox); form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;

                if (form.ShowDialog(this) != DialogResult.OK)
                    return false;

                item.ClassCode = NormalizeClassCode(codeBox.Text);
                item.DisplayName = (nameBox.Text ?? "").Trim();
                item.Aliases = (aliasesBox.Text ?? "")
                    .Split(new char[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                item.Active = activeBox.Checked;

                if (string.IsNullOrWhiteSpace(item.ClassCode) ||
                    string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    MessageBox.Show(
                        "CLASS CODE và TÊN CHUẨN không được để trống.",
                        "CLASS DICTIONARY");
                    return false;
                }

                return true;
            }
        }

        private string PromptAdminKey()
        {
            if (!string.IsNullOrWhiteSpace(_adminKey))
                return _adminKey;

            string value = PromptText(
                "ADMIN KEY - AI DATASET",
                "Nhập COMPANY ADMIN KEY. Key chỉ giữ trong RAM phiên này:",
                "",
                true);

            if (value == null)
                return "";

            _adminKey = value.Trim();
            return _adminKey;
        }

        private string PromptText(
            string title,
            string label,
            string initial,
            bool password)
        {
            using (Form form = new Form())
            using (Label l = new Label())
            using (TextBox box = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.Width = 660;
                form.Height = 220;
                form.BackColor = Color.White;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                l.Left = 20; l.Top = 24; l.Width = 600; l.Height = 28; l.Text = label;
                box.Left = 20; box.Top = 60; box.Width = 600; box.Text = initial ?? "";
                box.UseSystemPasswordChar = password;

                ok.Left = 385; ok.Top = 112; ok.Width = 110; ok.Height = 36;
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
                cancel.Left = 510; cancel.Top = 112; cancel.Width = 110; cancel.Height = 36;
                cancel.Text = "HỦY"; cancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(l); form.Controls.Add(box); form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;

                if (form.ShowDialog(this) != DialogResult.OK)
                    return null;

                return box.Text ?? "";
            }
        }

        private static string BuildClassCode(string label)
        {
            string value = (label ?? "").Trim().ToUpperInvariant();
            string normalized = value.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();

            foreach (char ch in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                char c = ch == 'Đ' ? 'D' : ch;

                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }

            return NormalizeClassCode(sb.ToString());
        }

        private static string NormalizeClassCode(string value)
        {
            string raw = (value ?? "").Trim().ToUpperInvariant();
            StringBuilder sb = new StringBuilder();

            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }

            return sb.ToString().Trim('_');
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Length <= 14
                ? value
                : value.Substring(0, 14) + "...";
        }

        private void SetBusy(bool busy, string text)
        {
            _refreshButton.Enabled = !busy;
            _classButton.Enabled = !busy;
            _approveButton.Enabled = !busy;
            _rejectButton.Enabled = !busy;
            _filterBox.Enabled = !busy;

            if (!string.IsNullOrWhiteSpace(text))
                _summaryLabel.Text = text;
        }

        private void DisposeOwnedBitmaps()
        {
            _preview.Image = null;

            foreach (Bitmap bitmap in _ownedBitmaps)
            {
                try
                {
                    bitmap?.Dispose();
                }
                catch
                {
                }
            }

            _ownedBitmaps.Clear();
        }
    }
}
