#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassLibrary4
{
    public sealed class AiGraphCloudManagerForm : Form
    {
        private readonly AiCloudConfig _config;
        private readonly AiGraphCloudClient _client;

        private readonly Label _summary = new Label();
        private readonly ComboBox _filter = new ComboBox();
        private readonly Button _refresh = new Button();
        private readonly Button _pullApproved = new Button();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _detail = new Label();
        private readonly Button _approve = new Button();
        private readonly Button _reject = new Button();
        private readonly Button _close = new Button();

        private List<AiGraphCloudReviewRow> _rows =
            new List<AiGraphCloudReviewRow>();

        private string _adminKey = "";
        private bool _busy;

        public AiGraphCloudManagerForm(
            AiCloudConfig config,
            AiGraphCloudClient client)
        {
            _config =
                config ??
                throw new ArgumentNullException(
                    nameof(config));

            _client =
                client ??
                throw new ArgumentNullException(
                    nameof(client));

            Text =
                "GNN GRAPH DATASET CLOUD - REVIEW / APPROVE";

            StartPosition =
                FormStartPosition.CenterScreen;

            Width =
                1420;

            Height =
                820;

            MinimumSize =
                new Size(
                    1150,
                    680);

            BackColor =
                Color.White;

            Font =
                new Font(
                    "Segoe UI",
                    9.5f);

            BuildUi();

            Shown +=
                async (s, e) =>
                {
                    await RefreshAsync();
                };
        }

        private void BuildUi()
        {
            Label title =
                new Label
                {
                    Left = 18,
                    Top = 14,
                    Width = 720,
                    Height = 30,
                    Text =
                        "GNN GRAPH DATASET CLOUD - REVIEW / APPROVE",
                    Font =
                        new Font(
                            "Segoe UI",
                            12.0f,
                            FontStyle.Bold),
                    ForeColor =
                        Color.FromArgb(
                            88,
                            28,
                            135)
                };

            _summary.Left =
                18;

            _summary.Top =
                47;

            _summary.Width =
                850;

            _summary.Height =
                26;

            _summary.ForeColor =
                Color.FromArgb(
                    71,
                    85,
                    105);

            Label filterLabel =
                new Label
                {
                    Left = 890,
                    Top = 22,
                    Width = 80,
                    Height = 24,
                    Text =
                        "TRẠNG THÁI:"
                };

            _filter.Left =
                975;

            _filter.Top =
                18;

            _filter.Width =
                160;

            _filter.DropDownStyle =
                ComboBoxStyle.DropDownList;

            _filter.Items.AddRange(
                new object[]
                {
                    "TẤT CẢ",
                    "PENDING",
                    "APPROVED",
                    "REJECTED"
                });

            _filter.SelectedIndex =
                0;

            _filter.SelectedIndexChanged +=
                async (s, e) =>
                {
                    if (Visible)
                    {
                        await RefreshAsync();
                    }
                };

            _refresh.Left =
                1150;

            _refresh.Top =
                16;

            _refresh.Width =
                105;

            _refresh.Height =
                32;

            _refresh.Text =
                "LÀM MỚI";

            _refresh.Click +=
                async (s, e) =>
                    await RefreshAsync();

            _pullApproved.Left =
                1265;

            _pullApproved.Top =
                16;

            _pullApproved.Width =
                125;

            _pullApproved.Height =
                32;

            _pullApproved.Text =
                "KÉO APPROVED";

            _pullApproved.BackColor =
                Color.FromArgb(
                    237,
                    233,
                    254);

            _pullApproved.ForeColor =
                Color.FromArgb(
                    91,
                    33,
                    182);

            _pullApproved.FlatStyle =
                FlatStyle.Flat;

            _pullApproved.Click +=
                async (s, e) =>
                    await PullApprovedAsync();

            _grid.Left =
                18;

            _grid.Top =
                82;

            _grid.Width =
                1040;

            _grid.Height =
                650;

            _grid.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Bottom |
                AnchorStyles.Left |
                AnchorStyles.Right;

            _grid.AllowUserToAddRows =
                false;

            _grid.AllowUserToDeleteRows =
                false;

            _grid.AllowUserToResizeRows =
                false;

            _grid.AutoGenerateColumns =
                false;

            _grid.ReadOnly =
                true;

            _grid.MultiSelect =
                false;

            _grid.SelectionMode =
                DataGridViewSelectionMode.FullRowSelect;

            _grid.RowHeadersVisible =
                false;

            _grid.BackgroundColor =
                Color.White;

            _grid.RowTemplate.Height =
                34;

            _grid.ColumnHeadersHeight =
                38;

            _grid.SelectionChanged +=
                (s, e) =>
                    UpdateDetail();

            AddTextColumn(
                "STT",
                "STT",
                55);

            AddTextColumn(
                "STATUS",
                "TRẠNG THÁI",
                170);

            AddTextColumn(
                "HASH",
                "GRAPH HASH",
                215);

            AddTextColumn(
                "PIPES",
                "ỐNG",
                75);

            AddTextColumn(
                "GT",
                "GT DN",
                80);

            AddTextColumn(
                "CLASSES",
                "DN CLASS",
                90);

            AddTextColumn(
                "VOTERS",
                "MÁY",
                70);

            AddTextColumn(
                "DNCOUNTS",
                "PHÂN BỐ DN",
                250);

            AddTextColumn(
                "UPDATED",
                "CẬP NHẬT",
                150);

            Panel right =
                new Panel
                {
                    Left = 1075,
                    Top = 82,
                    Width = 315,
                    Height = 650,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Bottom |
                        AnchorStyles.Right,
                    BackColor =
                        Color.FromArgb(
                            248,
                            250,
                            252),
                    BorderStyle =
                        BorderStyle.FixedSingle
                };

            Label detailTitle =
                new Label
                {
                    Left = 14,
                    Top = 14,
                    Width = 280,
                    Height = 28,
                    Text =
                        "GRAPH ĐANG CHỌN",
                    Font =
                        new Font(
                            "Segoe UI",
                            10.5f,
                            FontStyle.Bold),
                    ForeColor =
                        Color.FromArgb(
                            51,
                            65,
                            85)
                };

            _detail.Left =
                14;

            _detail.Top =
                50;

            _detail.Width =
                282;

            _detail.Height =
                350;

            _detail.ForeColor =
                Color.FromArgb(
                    51,
                    65,
                    85);

            _approve.Left =
                14;

            _approve.Top =
                435;

            _approve.Width =
                282;

            _approve.Height =
                42;

            _approve.Text =
                "DUYỆT GRAPH";

            _approve.Font =
                new Font(
                    "Segoe UI",
                    9.5f,
                    FontStyle.Bold);

            _approve.BackColor =
                Color.FromArgb(
                    220,
                    252,
                    231);

            _approve.ForeColor =
                Color.FromArgb(
                    22,
                    101,
                    52);

            _approve.FlatStyle =
                FlatStyle.Flat;

            _approve.Click +=
                async (s, e) =>
                    await ReviewSelectedAsync(
                        "APPROVE");

            _reject.Left =
                14;

            _reject.Top =
                487;

            _reject.Width =
                282;

            _reject.Height =
                42;

            _reject.Text =
                "LOẠI GRAPH";

            _reject.Font =
                new Font(
                    "Segoe UI",
                    9.5f,
                    FontStyle.Bold);

            _reject.BackColor =
                Color.FromArgb(
                    254,
                    226,
                    226);

            _reject.ForeColor =
                Color.FromArgb(
                    185,
                    28,
                    28);

            _reject.FlatStyle =
                FlatStyle.Flat;

            _reject.Click +=
                async (s, e) =>
                    await ReviewSelectedAsync(
                        "REJECT");

            Label hint =
                new Label
                {
                    Left = 14,
                    Top = 545,
                    Width = 282,
                    Height = 70,
                    Text =
                        "Admin chỉ duyệt Graph có Ground-truth DN hợp lý.\n" +
                        "Consensus 3 máy có thể tự APPROVED.\n" +
                        "GRAPH_NEIGHBOR không được dùng làm target train.",
                    ForeColor =
                        Color.FromArgb(
                            100,
                            116,
                            139)
                };

            _close.Left =
                1215;

            _close.Top =
                742;

            _close.Width =
                175;

            _close.Height =
                38;

            _close.Anchor =
                AnchorStyles.Bottom |
                AnchorStyles.Right;

            _close.Text =
                "ĐÓNG";

            _close.Click +=
                (s, e) =>
                    Close();

            right.Controls.Add(
                detailTitle);

            right.Controls.Add(
                _detail);

            right.Controls.Add(
                _approve);

            right.Controls.Add(
                _reject);

            right.Controls.Add(
                hint);

            Controls.Add(
                title);

            Controls.Add(
                _summary);

            Controls.Add(
                filterLabel);

            Controls.Add(
                _filter);

            Controls.Add(
                _refresh);

            Controls.Add(
                _pullApproved);

            Controls.Add(
                _grid);

            Controls.Add(
                right);

            Controls.Add(
                _close);

            Resize +=
                (s, e) =>
                {
                    _grid.Width =
                        Math.Max(
                            650,
                            ClientSize.Width -
                            380);

                    right.Left =
                        ClientSize.Width -
                        335;

                    _close.Left =
                        ClientSize.Width -
                        205;

                    _close.Top =
                        ClientSize.Height -
                        60;

                    _grid.Height =
                        ClientSize.Height -
                        150;

                    right.Height =
                        ClientSize.Height -
                        150;
                };
        }

        private void AddTextColumn(
            string name,
            string header,
            int width)
        {
            _grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name =
                        name,
                    HeaderText =
                        header,
                    Width =
                        width,
                    SortMode =
                        DataGridViewColumnSortMode.NotSortable
                });
        }

        private async Task RefreshAsync()
        {
            if (_busy)
                return;

            _busy =
                true;

            SetBusy(
                true);

            try
            {
                string filter =
                    ResolveFilter();

                _rows =
                    await _client
                        .GetReviewRowsAsync(
                            _config,
                            filter,
                            500);

                AiGraphCloudSummary summary =
                    await _client
                        .GetCloudSummaryAsync(
                            _config);

                _summary.Text =
                    "Cloud " +
                    summary.TotalGraphs +
                    " graph  |  Approved " +
                    summary.Approved +
                    "  |  Pending " +
                    summary.Pending +
                    "  |  Rejected " +
                    summary.Rejected +
                    "  |  Reliable DN targets " +
                    summary.TotalReliableTargets +
                    "  |  Submissions " +
                    summary.TotalSubmissions;

                FillGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Không tải được Graph Cloud:\n\n" +
                    ex.Message,
                    "GNN GRAPH CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _busy =
                    false;

                SetBusy(
                    false);
            }
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();

            int stt =
                1;

            foreach (AiGraphCloudReviewRow row
                in _rows)
            {
                int index =
                    _grid.Rows.Add();

                DataGridViewRow gridRow =
                    _grid.Rows[index];

                gridRow.Tag =
                    row;

                gridRow.Cells["STT"].Value =
                    stt++;

                gridRow.Cells["STATUS"].Value =
                    row.Status;

                gridRow.Cells["HASH"].Value =
                    ShortHash(
                        row.GraphHash);

                gridRow.Cells["PIPES"].Value =
                    row.PipeCount;

                gridRow.Cells["GT"].Value =
                    row.ExplicitLabelCount;

                gridRow.Cells["CLASSES"].Value =
                    row.DnClassCount;

                gridRow.Cells["VOTERS"].Value =
                    row.VoterCount;

                gridRow.Cells["DNCOUNTS"].Value =
                    FormatDnCounts(
                        row.DnCounts);

                gridRow.Cells["UPDATED"].Value =
                    FormatDate(
                        row.UpdatedAt);

                ApplyStatusColor(
                    gridRow,
                    row.Status);
            }

            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected =
                    true;
            }

            UpdateDetail();
        }

        private void ApplyStatusColor(
            DataGridViewRow row,
            string status)
        {
            string s =
                (status ?? "")
                    .ToUpperInvariant();

            if (s.StartsWith(
                    "APPROVED"))
            {
                row.DefaultCellStyle.BackColor =
                    Color.FromArgb(
                        240,
                        253,
                        244);

                return;
            }

            if (s.StartsWith(
                    "REJECTED"))
            {
                row.DefaultCellStyle.BackColor =
                    Color.FromArgb(
                        254,
                        242,
                        242);

                return;
            }

            row.DefaultCellStyle.BackColor =
                Color.FromArgb(
                    255,
                    251,
                    235);
        }

        private void UpdateDetail()
        {
            AiGraphCloudReviewRow row =
                GetSelected();

            if (row == null)
            {
                _detail.Text =
                    "Chọn một Graph trong bảng.";

                return;
            }

            _detail.Text =
                "TRẠNG THÁI: " +
                row.Status +
                "\n\nHASH:\n" +
                row.GraphHash +
                "\n\nỐng: " +
                row.PipeCount +
                "\nGround-truth DN: " +
                row.ExplicitLabelCount +
                "\nDN classes: " +
                row.DnClassCount +
                "\nMáy / voter: " +
                row.VoterCount +
                "\n\nPHÂN BỐ DN:\n" +
                FormatDnCounts(
                    row.DnCounts,
                    true) +
                "\n\nGHI CHÚ:\n" +
                (string.IsNullOrWhiteSpace(
                     row.ReviewNote)
                    ? "-"
                    : row.ReviewNote);
        }

        private async Task ReviewSelectedAsync(
            string action)
        {
            AiGraphCloudReviewRow row =
                GetSelected();

            if (row == null)
                return;

            if (_busy)
                return;

            if (string.IsNullOrWhiteSpace(
                    _adminKey))
            {
                _adminKey =
                    PromptPassword(
                        "ADMIN KEY - GNN GRAPH CLOUD",
                        "Nhập Company Admin Key:");

                if (string.IsNullOrWhiteSpace(
                        _adminKey))
                {
                    return;
                }
            }

            string note =
                PromptText(
                    action == "APPROVE"
                        ? "DUYỆT GRAPH"
                        : "LOẠI GRAPH",
                    "Ghi chú (có thể để trống):",
                    row.ReviewNote ?? "");

            if (note == null)
                return;

            _busy =
                true;

            SetBusy(
                true);

            try
            {
                AiGraphCloudAdminResult result =
                    await _client
                        .AdminReviewAsync(
                            _config,
                            _adminKey,
                            row.GraphHash,
                            action,
                            note,
                            _config.VoterId);

                if (!result.Ok)
                {
                    string error =
                        !string.IsNullOrWhiteSpace(
                            result.Error)
                            ? result.Error
                            : result.Message;

                    if (string.Equals(
                            error,
                            "INVALID_ADMIN_KEY",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _adminKey =
                            "";
                    }

                    MessageBox.Show(
                        this,
                        "Cloud không duyệt thao tác:\n\n" +
                        error,
                        "GNN GRAPH CLOUD",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                await RefreshAsyncCore();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "GNN GRAPH CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _busy =
                    false;

                SetBusy(
                    false);
            }
        }

        private async Task RefreshAsyncCore()
        {
            string filter =
                ResolveFilter();

            _rows =
                await _client
                    .GetReviewRowsAsync(
                        _config,
                        filter,
                        500);

            AiGraphCloudSummary summary =
                await _client
                    .GetCloudSummaryAsync(
                        _config);

            _summary.Text =
                "Cloud " +
                summary.TotalGraphs +
                " graph  |  Approved " +
                summary.Approved +
                "  |  Pending " +
                summary.Pending +
                "  |  Rejected " +
                summary.Rejected +
                "  |  Reliable DN targets " +
                summary.TotalReliableTargets;

            FillGrid();
        }

        private async Task PullApprovedAsync()
        {
            if (_busy)
                return;

            _busy =
                true;

            SetBusy(
                true);

            try
            {
                int saved =
                    await _client
                        .PullApprovedAsync(
                            _config);

                MessageBox.Show(
                    this,
                    "Đã đồng bộ Approved Graph về máy.\n\n" +
                    "File mới/cập nhật: " +
                    saved,
                    "GNN GRAPH CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "GNN GRAPH CLOUD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _busy =
                    false;

                SetBusy(
                    false);
            }
        }

        private AiGraphCloudReviewRow GetSelected()
        {
            if (_grid.SelectedRows.Count == 0)
                return null;

            return
                _grid
                    .SelectedRows[0]
                    .Tag as AiGraphCloudReviewRow;
        }

        private string ResolveFilter()
        {
            string text =
                (_filter.SelectedItem?.ToString() ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (text == "TẤT CẢ")
                return "ALL";

            return text;
        }

        private void SetBusy(
            bool busy)
        {
            _refresh.Enabled =
                !busy;

            _pullApproved.Enabled =
                !busy;

            _approve.Enabled =
                !busy;

            _reject.Enabled =
                !busy;

            Cursor =
                busy
                    ? Cursors.WaitCursor
                    : Cursors.Default;
        }

        private static string ShortHash(
            string hash)
        {
            if (string.IsNullOrWhiteSpace(
                    hash))
            {
                return "";
            }

            return
                hash.Length <= 18
                    ? hash
                    : hash.Substring(
                        0,
                        18) +
                      "...";
        }

        private static string FormatDnCounts(
            Dictionary<string, int> counts,
            bool multiline = false)
        {
            if (counts == null ||
                counts.Count == 0)
            {
                return "-";
            }

            string separator =
                multiline
                    ? "\n"
                    : "  •  ";

            return
                string.Join(
                    separator,
                    counts
                        .OrderBy(
                            x =>
                                DnNumber(
                                    x.Key))
                        .ThenBy(
                            x =>
                                x.Key)
                        .Select(
                            x =>
                                x.Key +
                                "=" +
                                x.Value));
        }

        private static int DnNumber(
            string value)
        {
            string digits =
                new string(
                    (value ?? "")
                        .Where(
                            char.IsDigit)
                        .ToArray());

            return
                int.TryParse(
                    digits,
                    out int number)
                    ? number
                    : int.MaxValue;
        }

        private static string FormatDate(
            string value)
        {
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime dt))
            {
                return
                    dt.ToLocalTime()
                        .ToString(
                            "dd/MM/yyyy HH:mm",
                            CultureInfo.InvariantCulture);
            }

            return
                value ?? "";
        }

        private static string PromptPassword(
            string title,
            string label)
        {
            using (Form form =
                new Form())
            using (Label text =
                new Label())
            using (TextBox box =
                new TextBox())
            using (Button ok =
                new Button())
            using (Button cancel =
                new Button())
            {
                form.Text =
                    title;

                form.StartPosition =
                    FormStartPosition.CenterParent;

                form.Width =
                    520;

                form.Height =
                    210;

                form.MinimizeBox =
                    false;

                form.MaximizeBox =
                    false;

                text.Left =
                    18;

                text.Top =
                    20;

                text.Width =
                    465;

                text.Height =
                    28;

                text.Text =
                    label;

                box.Left =
                    18;

                box.Top =
                    55;

                box.Width =
                    465;

                box.UseSystemPasswordChar =
                    true;

                ok.Left =
                    285;

                ok.Top =
                    105;

                ok.Width =
                    95;

                ok.Height =
                    34;

                ok.Text =
                    "OK";

                ok.DialogResult =
                    DialogResult.OK;

                cancel.Left =
                    388;

                cancel.Top =
                    105;

                cancel.Width =
                    95;

                cancel.Height =
                    34;

                cancel.Text =
                    "HỦY";

                cancel.DialogResult =
                    DialogResult.Cancel;

                form.Controls.AddRange(
                    new Control[]
                    {
                        text,
                        box,
                        ok,
                        cancel
                    });

                form.AcceptButton =
                    ok;

                form.CancelButton =
                    cancel;

                if (form.ShowDialog() !=
                    DialogResult.OK)
                {
                    return "";
                }

                return
                    (box.Text ?? "")
                        .Trim();
            }
        }

        private static string PromptText(
            string title,
            string label,
            string initial)
        {
            using (Form form =
                new Form())
            using (Label text =
                new Label())
            using (TextBox box =
                new TextBox())
            using (Button ok =
                new Button())
            using (Button cancel =
                new Button())
            {
                form.Text =
                    title;

                form.StartPosition =
                    FormStartPosition.CenterParent;

                form.Width =
                    560;

                form.Height =
                    280;

                form.MinimizeBox =
                    false;

                form.MaximizeBox =
                    false;

                text.Left =
                    18;

                text.Top =
                    16;

                text.Width =
                    505;

                text.Height =
                    26;

                text.Text =
                    label;

                box.Left =
                    18;

                box.Top =
                    48;

                box.Width =
                    505;

                box.Height =
                    120;

                box.Multiline =
                    true;

                box.ScrollBars =
                    ScrollBars.Vertical;

                box.Text =
                    initial ?? "";

                ok.Left =
                    325;

                ok.Top =
                    185;

                ok.Width =
                    95;

                ok.Height =
                    34;

                ok.Text =
                    "OK";

                ok.DialogResult =
                    DialogResult.OK;

                cancel.Left =
                    428;

                cancel.Top =
                    185;

                cancel.Width =
                    95;

                cancel.Height =
                    34;

                cancel.Text =
                    "HỦY";

                cancel.DialogResult =
                    DialogResult.Cancel;

                form.Controls.AddRange(
                    new Control[]
                    {
                        text,
                        box,
                        ok,
                        cancel
                    });

                form.AcceptButton =
                    ok;

                form.CancelButton =
                    cancel;

                if (form.ShowDialog() !=
                    DialogResult.OK)
                {
                    return null;
                }

                return
                    box.Text ?? "";
            }
        }
    }
}
