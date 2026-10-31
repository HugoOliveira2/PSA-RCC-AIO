using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace AioAssist
{
    public sealed class AioTranslations
    {
        private Dictionary<string, object> values, fields;
        public static AioTranslations Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("aio-pt.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var root = OfflineData.Object(OfflineData.Serializer().DeserializeObject(reader.ReadToEnd()));
                return new AioTranslations { values = OfflineData.Object(root["values"]), fields = OfflineData.Object(root["fields"]) };
            }
        }
        public string Name(AioField f)
        {
            if (fields.ContainsKey(f.Name)) return Convert.ToString(fields[f.Name]);
            if (f.Name.StartsWith("MENU_AMBIANCE_")) return "Opção de tema " + f.Name.Split('_')[2] + " no menu";
            return f.Description == f.Name ? "Identificador técnico — sem descrição na fonte" : f.Description + " (original)";
        }
        public string Value(AioField field, ulong raw)
        {
            if (field.Choices.ContainsKey(raw))
            {
                string original = field.Choices[raw];
                if (values.ContainsKey(original)) return Convert.ToString(values[original]);
                if (original == "Ativado / presente" || original == "Desativado / ausente") return original;
                return original + " (original)";
            }
            if (field.Name == "AMBIANCE_DEFAULT_000") return "Código de tema " + raw + " — nome visual não informado no mapa";
            if (field.Type == "bool" && raw <= 1) return raw == 0 ? "Desativado / ausente" : "Ativado / presente";
            if (field.Choices.Count > 0) return "Valor não descrito na tabela do mapa";
            return "Valor bruto — sem tabela de significados na fonte";
        }
        public static string Category(AioField f)
        {
            string n = f.Name;
            if (n.Contains("AMBIANCE") || n.Contains("FAMILY") || n.Contains("MOOD")) return "Temas";
            if (n.Contains("AIR_") || n.Contains("CLIM") || n.Contains("TEMPERATURE") || n.Contains("PRECONDITION")) return "Climatização";
            if (n.Contains("CAMERA") || n.Contains("REVERS") || n.Contains("RVC")) return "Câmeras";
            if (new[] { "BLUETOOTH", "WI_FI", "USB", "CARPLAY", "INTERNET", "AAP_", "SMARTAPPS", "MIRROR" }.Any(n.Contains)) return "Conectividade";
            if (new[] { "RADIO", "RDS", "WAVEBAND", "TMC", "AERIAL" }.Any(n.Contains)) return "Rádio";
            if (new[] { "AUDIO", "SOUND", "VOLUME", "ACV", "HIFI", "MICROPHONE", "FADER", "CD_" }.Any(n.Contains)) return "Áudio";
            if (new[] { "PARK", "CRUISE", "DISTANCE", "TRACTION", "BLIND", "LANE", "WARNING", "DETECTION" }.Any(n.Contains)) return "Assistências";
            return "Veículo e outros";
        }
    }
    public sealed class AioOfflineRow
    {
        public AioField Field;
        public string Name, Category, Raw, Meaning, Reference, Difference;
        public bool Changed;
    }
    public sealed class AioOfflineModel
    {
        public readonly AioMap Map = AioMap.Load();
        public readonly AioTranslations Translations = AioTranslations.Load();
        public Snapshot Current, Reference;
        public List<AioOfflineRow> Rows = new List<AioOfflineRow>();
        public int ChangedBytes;
        public string Identity(Snapshot snapshot)
        {
            if (snapshot == null || !snapshot.Data.ContainsKey("FE") || !snapshot.Data.ContainsKey("B0") || !snapshot.Data.ContainsKey("B1"))
                throw new FormatException("A leitura precisa conter FE, VIN (B0) e B1 completos.");
            return Map.Identity(snapshot.Data["FE"], snapshot.Data["B0"], snapshot.Data["B1"]);
        }
        public void SetCurrent(Snapshot snapshot) { Identity(snapshot); Current = snapshot; Reference = null; Rebuild(); }
        public void SetReference(Snapshot snapshot) { Identity(snapshot); if (Current == null) throw new InvalidOperationException("Abra uma leitura base primeiro."); Reference = snapshot; Rebuild(); }
        public void Rebuild()
        {
            Rows.Clear(); ChangedBytes = 0; if (Current == null) return;
            var current = AioMap.Bytes(Current.Data["B1"]); var other = Reference == null ? null : AioMap.Bytes(Reference.Data["B1"]);
            if (other != null) ChangedBytes = current.Where((b, i) => b != other[i]).Count();
            foreach (var f in Map.Fields)
            {
                if (f.Width > 64) continue;
                ulong raw = f.Read(current), reference = other == null ? 0 : f.Read(other);
                bool changed = other != null && raw != reference;
                Rows.Add(new AioOfflineRow { Field = f, Name = Translations.Name(f), Category = AioTranslations.Category(f),
                    Raw = raw + " (0x" + raw.ToString("X") + ")", Meaning = Translations.Value(f, raw),
                    Reference = other == null ? "—" : reference + " — " + Translations.Value(f, reference),
                    Changed = changed, Difference = other == null ? "Sem comparação" : changed ? "Diferente" : "Igual" });
            }
        }
        public string Csv(IEnumerable<AioOfflineRow> rows)
        {
            var text = new StringBuilder();
            text.AppendLine("Categoria;Parâmetro;Identificador;Bytes.bits;Valor na base;Significado;Outra leitura;Comparação");
            foreach (var row in rows)
                text.AppendLine(string.Join(";", new[] { row.Category, row.Name, row.Field.Name, row.Field.Bits, row.Raw, row.Meaning, row.Reference, row.Difference }
                    .Select(v => "\"" + v.Replace("\"", "\"\"") + "\"").ToArray()));
            return text.ToString();
        }
    }
    // Deliberately has no Session, ITransport or serial-port dependency.
    public sealed class AioOfflineView : Form
    {
        private readonly AioOfflineModel model = new AioOfflineModel();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label identity = new Label(), summary = new Label();
        private readonly TextBox search = new TextBox(), detail = new TextBox();
        private readonly ComboBox category = new ComboBox();
        private readonly CheckBox onlyChanges = new CheckBox();
        private readonly Button reference = new Button(), clearReference = new Button(), export = new Button();
        private List<AioOfflineRow> shown = new List<AioOfflineRow>();
        private static readonly HashSet<string> highlights = new HashSet<string>(new[] {
            "MANUFACTURER_S_BRAND", "VEHICLE_POWER_SOURCE", "AMBIANCE_DEFAULT_000", "REVERSING_ASSISTANCE_CAMERA",
            "AIR_CONDITIONING_FUNCTION", "TYPE_AIR_CONDITIONING_MIXING", "TYPE_AIR_CONDITIONING", "AUTOMATIC_AIR_CONDITIONING_BUTTON",
            "TYPE_AIR_CONDITIONING_REGULATION", "CARPLAY_FUNCTION", "BLUETOOTH_FUNCTION", "GEOGRAPHIC_AREA_USE_RADIO" });
        public AioOfflineView(bool loadIncluded)
        {
            Text = "AIO Assist 0.6.1 — Configuração offline traduzida"; Size = new Size(1270, 830); MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            foreach (int height in new[] { 43, 78, 42 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); Controls.Add(root);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var open = Button("Abrir leitura JSON", 165); var latest = Button("Última leitura incluída", 195);
            reference.Text = "Comparar com JSON"; reference.Width = 180; reference.Height = 32;
            clearReference.Text = "Limpar comparação"; clearReference.Width = 180; clearReference.Height = 32;
            export.Text = "Exportar tabela CSV"; export.Width = 175; export.Height = 32;
            var crossButton = Button("Cruzamento AIO × NAC", 190);
            buttons.Controls.AddRange(new Control[] { open, latest, reference, clearReference, export, crossButton }); root.Controls.Add(buttons, 0, 0);
            identity.Dock = DockStyle.Fill; identity.Text = "Abra um JSON para ver a configuração registrada. Esta tela não abre portas seriais."; root.Controls.Add(identity, 0, 1);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            category.DropDownStyle = ComboBoxStyle.DropDownList; category.Width = 200;
            category.Items.AddRange(new object[] { "Destaques", "Todos", "Temas", "Climatização", "Câmeras", "Conectividade", "Áudio", "Rádio", "Assistências", "Veículo e outros" });
            category.SelectedIndex = 0; search.Width = 360;
            onlyChanges.Text = "Somente diferentes"; onlyChanges.AutoSize = true;
            filters.Controls.Add(category); filters.Controls.Add(new Label { Text = "Buscar:", AutoSize = true, Padding = new Padding(8, 4, 0, 0) }); filters.Controls.Add(search); filters.Controls.Add(onlyChanges); root.Controls.Add(filters, 0, 2);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
            foreach (string title in new[] { "Parâmetro", "Valor na base", "Significado em português", "Outra leitura", "Comparação" }) grid.Columns.Add(title, title);
            grid.Columns[0].FillWeight = 110; grid.Columns[1].FillWeight = 42; grid.Columns[2].FillWeight = 145; grid.Columns[3].FillWeight = 115; grid.Columns[4].FillWeight = 47;
            grid.BackgroundColor = Color.White; root.Controls.Add(grid, 0, 3);
            detail.Dock = DockStyle.Fill; detail.Multiline = true; detail.ReadOnly = true; detail.ScrollBars = ScrollBars.Vertical; root.Controls.Add(detail, 0, 4);
            summary.Dock = DockStyle.Fill; root.Controls.Add(summary, 0, 5);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Somente consulta • Valores registrados no arquivo, não uma leitura atual da AIO • Interpretação experimental do mapa PSA-RE." }, 0, 6);
            open.Click += delegate { Choose(false); }; latest.Click += delegate { LoadIncluded(); }; reference.Click += delegate { Choose(true); };
            clearReference.Click += delegate { model.Reference = null; model.Rebuild(); onlyChanges.Checked = false; Populate(); };
            category.SelectedIndexChanged += delegate { Populate(); }; search.TextChanged += delegate { Populate(); }; onlyChanges.CheckedChanged += delegate { Populate(); };
            grid.SelectionChanged += delegate { ShowDetail(); }; export.Click += delegate { Export(); };
            crossButton.Click += delegate {
                if (model.Current == null) { MessageBox.Show(this, "Abra uma leitura AIO primeiro."); return; }
                try { using (var viewer = new AioCrossView(model.Current)) viewer.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cruzamento offline"); }
            };
            if (loadIncluded) LoadIncluded(); else Populate();
        }
        private static Button Button(string label, int width) { return new Button { Text = label, Width = width, Height = 32 }; }
        public void LoadFile(string path, bool compare)
        {
            var snapshot = OfflineData.Load(path);
            if (compare) model.SetReference(snapshot); else { model.SetCurrent(snapshot); onlyChanges.Checked = false; }
            Populate();
        }
        private void LoadIncluded()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "examples", "leitura-anonimizada.json");
            try { if (!File.Exists(path)) throw new FileNotFoundException("Leitura incluída não encontrada. Use Abrir leitura JSON."); LoadFile(path, false); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Consulta offline"); }
        }
        private void Choose(bool compare)
        {
            using (var dialog = new OpenFileDialog { Filter = "Leitura/backup AIO (*.json)|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { LoadFile(dialog.FileName, compare); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Arquivo não carregado"); }
            }
        }
        private void Populate()
        {
            bool hasData = model.Current != null, hasReference = model.Reference != null;
            reference.Enabled = hasData; clearReference.Enabled = hasReference; onlyChanges.Enabled = hasReference; export.Enabled = hasData;
            string selectedCategory = Convert.ToString(category.SelectedItem);
            shown = model.Rows.Where(r => (selectedCategory == "Todos" || (selectedCategory == "Destaques" ? highlights.Contains(r.Field.Name) : r.Category == selectedCategory)) &&
                (!onlyChanges.Checked || r.Changed) && (r.Name + " " + r.Field.Name + " " + r.Meaning + " " + r.Reference).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            grid.Rows.Clear();
            foreach (var row in shown)
            {
                int i = grid.Rows.Add(row.Name, row.Raw, row.Meaning, row.Reference, row.Difference); grid.Rows[i].Tag = row;
                if (row.Changed) grid.Rows[i].DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
            }
            grid.Columns[3].Visible = hasReference; grid.Columns[4].Visible = hasReference;
            if (hasData)
            {
                identity.Text = "BASE: " + Path.GetFileName(model.Current.Name) + " | " + model.Current.Summary + Environment.NewLine +
                    "VIN: " + Protocol.Ascii(model.Current.Data["B0"]) + " | Variante: " + model.Identity(model.Current) + " | B1: 180 bytes";
                if (hasReference) identity.Text += Environment.NewLine + "OUTRA LEITURA: " + Path.GetFileName(model.Reference.Name) + " | " + model.Reference.Summary;
                summary.Text = shown.Count + " / " + model.Rows.Count + " parâmetros exibidos. " + (hasReference ? model.Rows.Count(r => r.Changed) + " parâmetros e " + model.ChangedBytes + " bytes diferentes no B1." : "Escolha Todos para ver os 292 parâmetros; use as categorias e a busca.");
                if (hasReference && (model.Current.Data["B0"] != model.Reference.Data["B0"] || model.Current.Data["FE"] != model.Reference.Data["FE"]))
                    summary.Text += "\nAtenção: VIN ou FE diferem; comparação entre identificações distintas.";
            }
            ShowDetail();
        }
        private void ShowDetail()
        {
            var row = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as AioOfflineRow;
            if (row == null) { detail.Clear(); return; }
            var f = row.Field;
            detail.Text = row.Category + " | " + f.Name + " | bytes.bits " + f.Bits + " (bytes começam em 1)" + Environment.NewLine +
                "Descrição original: " + f.Description + Environment.NewLine + "Na base: " + row.Raw + " → " + row.Meaning + Environment.NewLine;
            if (model.Reference != null) detail.AppendText("Outra leitura: " + row.Reference + Environment.NewLine);
            if (f.Choices.Count > 0) detail.AppendText("Opções do mapa: " + string.Join(" | ", f.Choices.OrderBy(c => c.Key).Select(c => c.Key + " = " + model.Translations.Value(f, c.Key)).ToArray()));
            else detail.AppendText("O mapa não fornece uma tabela de nomes para os valores deste campo. O número exibido é bruto.");
        }
        private void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "Tabela CSV (*.csv)|*.csv", FileName = "aio-configuracao-offline.csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, model.Csv(shown), new UTF8Encoding(true)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Exportação"); }
            }
        }
        public void ShowCategory(string name) { category.SelectedItem = name; }
    }
}
