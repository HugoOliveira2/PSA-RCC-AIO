using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AioAssist
{
    public sealed class AioField
    {
        public string Name, Description, Bits, Type;
        public int Start, Width;
        public bool Editable;
        public readonly Dictionary<ulong, string> Choices = new Dictionary<ulong, string>();
        public ulong Max { get { return Width >= 64 ? ulong.MaxValue : (1UL << Width) - 1; } }
        public ulong Read(byte[] data)
        {
            if (Width > 64 || Start + Width > data.Length * 8) throw new FormatException("Campo fora do bloco ou maior que 64 bits.");
            ulong value = 0;
            for (int i = Start; i < Start + Width; i++) value = (value << 1) | (ulong)(byte)((data[i / 8] >> (7 - i % 8)) & 1);
            return value;
        }
        public void Put(byte[] data, ulong value)
        {
            if (!Editable || value > Max || (Choices.Count > 0 && !Choices.ContainsKey(value)))
                throw new InvalidOperationException("Valor não permitido pelo mapa: " + Name);
            for (int j = 0; j < Width; j++)
            {
                int i = Start + j, mask = 1 << (7 - i % 8);
                data[i / 8] = (byte)((data[i / 8] & ~mask) | (((value >> (Width - j - 1)) & 1) != 0 ? mask : 0));
            }
        }
        public string Label(ulong value) { return value + (Choices.ContainsKey(value) ? " — " + Choices[value] : ""); }
    }

    public sealed class AioMap
    {
        public string SourceHash;
        public readonly List<AioField> Fields = new List<AioField>();
        public readonly HashSet<string> Parts = new HashSet<string>();
        public readonly Dictionary<string, string> Variants = new Dictionary<string, string>();
        public static byte[] Bytes(string hex)
        {
            if (!Regex.IsMatch(hex ?? "", "^[0-9A-Fa-f]{360}$")) throw new FormatException("B1 deve conter exatamente 180 bytes.");
            return Enumerable.Range(0, 180).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        }
        public static string Hex(byte[] data) { return BitConverter.ToString(data).Replace("-", ""); }
        public static AioMap Load()
        {
            string json;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("aio-b1.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) json = reader.ReadToEnd();
            var root = OfflineData.Object(OfflineData.Serializer().DeserializeObject(json));
            if (OfflineData.StringValue(root, "schema") != "aio-b1-map-v1" || Convert.ToInt32(root["length"]) != 180)
                throw new FormatException("Mapa AIO inválido.");
            var map = new AioMap { SourceHash = OfflineData.StringValue(root, "sourceSha256") };
            foreach (var item in (object[])root["fields"])
            {
                var p = OfflineData.Object(item);
                string bits = OfflineData.StringValue(p, "bits");
                var m = Regex.Match(bits, @"^(\d+)\.([0-7])(?:-(\d+)\.([0-7]))?$");
                if (!m.Success) throw new FormatException("Posição inválida no mapa: " + bits);
                int start = (int.Parse(m.Groups[1].Value) - 1) * 8 + 7 - int.Parse(m.Groups[2].Value);
                int end = m.Groups[3].Success ? (int.Parse(m.Groups[3].Value) - 1) * 8 + 7 - int.Parse(m.Groups[4].Value) : start;
                if (start < 0 || end < start || end >= 1440) throw new FormatException("Mapa fora de B1.");
                var f = new AioField { Name = OfflineData.StringValue(p, "name"), Description = OfflineData.StringValue(p, "description"),
                    Bits = bits, Type = OfflineData.StringValue(p, "type"), Start = start, Width = end - start + 1 };
                f.Editable = f.Width <= 32 && (f.Type == "bool" || f.Type == "uint" || f.Type == "enum");
                foreach (var c in OfflineData.Object(p["choices"])) f.Choices.Add(ulong.Parse(c.Key, CultureInfo.InvariantCulture), Convert.ToString(c.Value));
                if (f.Type == "bool" && f.Choices.Count == 0) { f.Choices.Add(0, "Desativado / ausente"); f.Choices.Add(1, "Ativado / presente"); }
                map.Fields.Add(f);
            }
            foreach (var a in (object[])root["alternatives"])
                foreach (var name in (object[])OfflineData.Object(a)["differing"])
                    foreach (var field in map.Fields.Where(f => f.Name == Convert.ToString(name))) field.Editable = false;
            // Ambiguous overlapping fields may be inspected, but cannot be edited.
            foreach (var f in map.Fields)
                if (map.Fields.Any(g => g != f && f.Start < g.Start + g.Width && g.Start < f.Start + f.Width)) f.Editable = false;
            foreach (var variant in OfflineData.Object(root["variants"]))
                foreach (var part in (object[])OfflineData.Object(variant.Value)["parts"])
                { map.Parts.Add(Convert.ToString(part)); map.Variants[Convert.ToString(part)] = variant.Key; }
            if (map.Fields.Count != 292 || map.Fields.Select(f => f.Name).Distinct().Count() != 292) throw new FormatException("Catálogo incompleto ou duplicado.");
            return map;
        }
        public byte[] Apply(byte[] original, IDictionary<string, ulong> changes)
        {
            var result = (byte[])original.Clone();
            if (original.Length != 180) throw new FormatException("Comprimento B1 incorreto.");
            foreach (var change in changes)
            {
                var field = Fields.Single(f => f.Name == change.Key);
                if (field.Read(original) != change.Value) field.Put(result, change.Value);
            }
            return result;
        }
        public string Identity(string fe, string vin, string b1)
        {
            Bytes(b1);
            if (!Regex.IsMatch(fe ?? "", "^[0-9A-F]{48}$") || !Regex.IsMatch(vin ?? "", "^([0-9A-F]{2}){17}$") ||
                !Regex.IsMatch(Protocol.Ascii(vin), "^[A-HJ-NPR-Z0-9]{17}$")) throw new FormatException("Identificação FE/VIN incompleta.");
            string part = fe.Substring(42, 6);
            if (!Parts.Contains(part)) throw new InvalidOperationException("Variante " + part + " não consta no mapa. Gravação bloqueada.");
            return part + " / " + Variants[part];
        }
        public string Diff(string original, string proposed)
        {
            var a = Bytes(original); var b = Bytes(proposed); var lines = new List<string>();
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i])
                lines.Add("Byte " + (i + 1) + " (offset " + i + "): " + a[i].ToString("X2") + " → " + b[i].ToString("X2") + "   XOR " + (a[i] ^ b[i]).ToString("X2"));
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    public sealed class AioWritePlan
    {
        public string Fe, Vin, Original, Proposed;
        public Dictionary<string, ulong> Changes;
        public void Validate(AioMap map)
        {
            map.Identity(Fe, Vin, Original); AioMap.Bytes(Proposed);
            if (Changes == null || Changes.Count == 0 || Original == Proposed) throw new InvalidOperationException("Nenhuma alteração preparada.");
            if (AioMap.Hex(map.Apply(AioMap.Bytes(Original), Changes)) != Proposed) throw new InvalidOperationException("Proposta não corresponde aos campos selecionados.");
        }
        public string SaveBackup(AioMap map, string path)
        {
            Validate(map);
            var zones = new[] { Protocol.ParseZone("FE", "61FE" + Fe), Protocol.ParseZone("B0", "61B0" + Vin), Protocol.ParseZone("B1", "61B1" + Original) };
            string json = OfflineData.Serializer().Serialize(new { schema = "aio-assist-diagnostic-v1", applicationVersion = "0.6.1",
                capturedAt = DateTimeOffset.Now.ToString("o"), protocol = "KWP C0", completeBackup = false,
                purpose = "B1 original antes da tentativa de gravação; não é backup de firmware", zones = zones,
                plannedB1 = Proposed, changes = Changes, mapSourceSha256 = map.SourceHash });
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            return path;
        }
    }

    public sealed class AioParameterForm : Form
    {
        private readonly AioMap map = AioMap.Load();
        private readonly Session session;
        private readonly Action<string> log;
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox search = new TextBox();
        private readonly TextBox detail = new TextBox();
        private readonly ComboBox choice = new ComboBox();
        private readonly NumericUpDown numeric = new NumericUpDown();
        private readonly Label status = new Label();
        private readonly Button read = new Button(), open = new Button(), stage = new Button(), reset = new Button(), write = new Button(), import = new Button();
        private readonly Dictionary<string, ulong> changes = new Dictionary<string, ulong>();
        private string fe, vin, original;
        private bool live, busy, uncertain;
        private readonly Button sendAgain = new Button();
        private AioWritePlan retryPlan;
        private string retryBackup;
        public AioParameterForm(Session activeSession, Action<string> logger)
        {
            session = activeSession; log = logger;
            Text = "AIO Assist 0.6.1 — Parâmetros B1 (experimental)"; Size = new Size(1220, 820); MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            foreach (var height in new[] { 56, 42, 44 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); Controls.Add(root);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Mapa público PSA-RE • 292 parâmetros • B1 = 180 bytes\nEdição experimental: a releitura verifica os bytes; o efeito visual e a persistência ainda precisam ser testados na central." }, 0, 0);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            ButtonSetup(read, "Ler AIO conectada", 175); ButtonSetup(open, "Abrir JSON offline", 170); ButtonSetup(import, "Importar valores de JSON", 210);
            ButtonSetup(sendAgain, "Reenviar gravação", 180);
            sendAgain.Click += async delegate { await Guard(SendAgain); };
            bar.Controls.AddRange(new Control[] { read, open, import, sendAgain }); root.Controls.Add(bar, 0, 1);
            var filter = new FlowLayoutPanel { Dock = DockStyle.Fill }; filter.Controls.Add(new Label { Text = "Buscar:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            search.Width = 440; filter.Controls.Add(search);
            var themes = new Button { Text = "Temas", Width = 95 }; themes.Click += delegate { search.Text = "AMBIANCE"; }; filter.Controls.Add(themes);
            var all = new Button { Text = "Todos", Width = 90 }; all.Click += delegate { search.Clear(); }; filter.Controls.Add(all); root.Controls.Add(filter, 0, 2);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("name", "Parâmetro"); grid.Columns.Add("bits", "Bytes.bits (base 1)"); grid.Columns.Add("current", "Lido (valor bruto)"); grid.Columns.Add("next", "Proposto");
            grid.Columns[0].FillWeight = 150; grid.Columns[1].FillWeight = 55; grid.Columns[2].FillWeight = 100; grid.Columns[3].FillWeight = 100; root.Controls.Add(grid, 0, 3);
            detail.Multiline = true; detail.ReadOnly = true; detail.Dock = DockStyle.Fill; detail.ScrollBars = ScrollBars.Vertical; root.Controls.Add(detail, 0, 4);
            var editbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.Width = 460; numeric.Width = 180; numeric.DecimalPlaces = 0;
            ButtonSetup(stage, "Preparar valor", 145); ButtonSetup(reset, "Descartar alterações", 190); ButtonSetup(write, "Revisar e gravar B1…", 200);
            editbar.Controls.AddRange(new Control[] { choice, numeric, stage, reset, write }); root.Controls.Add(editbar, 0, 5);
            status.Dock = DockStyle.Fill; status.Text = "Leia a AIO ou abra uma leitura JSON. Arquivos offline nunca habilitam gravação."; root.Controls.Add(status, 0, 6);
            search.TextChanged += delegate { Populate(); }; grid.SelectionChanged += delegate { SelectField(); };
            read.Click += async delegate { await Guard(ReadLive); };
            open.Click += delegate { OpenOffline(); }; import.Click += delegate { ImportValues(); };
            stage.Click += delegate { Stage(); }; reset.Click += delegate { retryPlan = null; changes.Clear(); Populate(); };
            write.Click += async delegate { await Guard(Write); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; status.Text = "Aguarde a operação terminar antes de fechar."; } };
            Populate();
        }
        private static void ButtonSetup(Button b, string text, int width) { b.Text = text; b.Width = width; b.Height = 32; }
        private AioField Selected { get { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as AioField; } }
        private void ControlsState()
        {
            sendAgain.Enabled = !busy && !uncertain && retryPlan != null && session != null && session.CanRetryLastWrite;
            read.Enabled = !busy && !uncertain && session != null && session.Ready;
            open.Enabled = !busy; import.Enabled = !busy && original != null;
            write.Enabled = !busy && !uncertain && live && changes.Count > 0 && session != null && session.Ready;
            stage.Enabled = !busy && original != null && Selected != null && Selected.Editable; reset.Enabled = !busy && changes.Count > 0;
            search.Enabled = !busy; grid.Enabled = !busy; choice.Enabled = !busy; numeric.Enabled = !busy;
        }
        private async Task Guard(Func<Task> action)
        {
            if (busy) return; busy = true; ControlsState();
            try { await action(); }
            catch (Exception ex) { log("Editor B1: " + ex.Message); status.Text = ex.Message; MessageBox.Show(this, ex.Message, "AIO Assist", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { busy = false; ControlsState(); }
        }
        private void Populate()
        {
            string selected = Selected == null ? "" : Selected.Name; grid.Rows.Clear();
            byte[] data = original == null ? null : AioMap.Bytes(original);
            foreach (var f in map.Fields.Where(f => (f.Name + " " + f.Description).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string value = data == null ? "—" : (f.Width <= 64 ? f.Label(f.Read(data)) : "Somente bytes");
                int row = grid.Rows.Add(f.Name, f.Bits, value, changes.ContainsKey(f.Name) ? f.Label(changes[f.Name]) : "—");
                grid.Rows[row].Tag = f;
                if (changes.ContainsKey(f.Name)) grid.Rows[row].DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
                if (selected == f.Name) grid.CurrentCell = grid.Rows[row].Cells[0];
            }
            SelectField(); ControlsState();
        }
        private void SelectField()
        {
            var f = Selected; choice.Items.Clear(); if (f == null) { ControlsState(); return; }
            detail.Text = f.Description + Environment.NewLine + f.Name + " | bits " + f.Bits + " | tipo " + f.Type +
                (f.Editable ? " | valores brutos (sem conversão de unidades)." : " | somente leitura: tipo, largura ou sobreposição não editável.");
            if (f.Name.IndexOf("AMBIANCE", StringComparison.Ordinal) >= 0) detail.AppendText(Environment.NewLine + "A fonte não nomeia os temas. AMBIANCE_DEFAULT usa código 0–7; MENU_AMBIANCE indica opções do menu. A relação visual e a necessidade de reinício precisam ser verificadas no hardware.");
            bool hasChoices = f.Choices.Count > 0;
            choice.Visible = hasChoices; numeric.Visible = !hasChoices;
            ulong current = original != null && f.Width <= 64 ? f.Read(AioMap.Bytes(original)) : 0;
            ulong desired = changes.ContainsKey(f.Name) ? changes[f.Name] : current;
            foreach (var c in f.Choices.OrderBy(c => c.Key))
            { int index = choice.Items.Add(new Choice { Value = c.Key, Text = f.Label(c.Key) }); if (c.Key == desired) choice.SelectedIndex = index; }
            numeric.Maximum = f.Width <= 32 ? (decimal)f.Max : 0; numeric.Value = Math.Min(numeric.Maximum, (decimal)desired);
            ControlsState();
        }
        private sealed class Choice { public ulong Value; public string Text; public override string ToString() { return Text; } }
        private void Stage()
        {
            try
            {
                var f = Selected; if (f == null || !f.Editable || original == null) return;
                if (f.Choices.Count > 0 && choice.SelectedItem == null) throw new InvalidOperationException("Escolha um valor descrito no mapa.");
                ulong value = f.Choices.Count > 0 ? ((Choice)choice.SelectedItem).Value : (ulong)numeric.Value;
                retryPlan = null;
                if (value == f.Read(AioMap.Bytes(original))) changes.Remove(f.Name); else changes[f.Name] = value;
                map.Apply(AioMap.Bytes(original), changes); Populate(); status.Text = changes.Count + " alteração(ões) preparada(s). Nada foi gravado.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }
        private async Task ReadLive()
        {
            if (changes.Count > 0 && MessageBox.Show(this, "Descartar as alterações preparadas e fazer nova leitura?", "Reler", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            retryPlan = null;
            live = false; original = null; changes.Clear(); Populate(); status.Text = "Lendo FE, VIN e B1 da AIO…";
            var f = await session.ReadAsync("FE", CancellationToken.None); var v = await session.ReadAsync("B0", CancellationToken.None); var b = await session.ReadAsync("B1", CancellationToken.None);
            string identity = map.Identity(f.Hex, v.Hex, b.Hex); fe = f.Hex; vin = v.Hex; original = b.Hex; live = true;
            Populate(); status.Text = "Leitura ao vivo • " + identity + " • VIN " + Protocol.Ascii(vin) + " • " + DateTime.Now.ToString("HH:mm:ss");
        }
        private void OpenOffline()
        {
            using (var dialog = new OpenFileDialog { Filter = "Leitura/backup AIO (*.json)|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var snapshot = OfflineData.Load(dialog.FileName); string f = snapshot.Data["FE"], v = snapshot.Data["B0"], b = snapshot.Data["B1"];
                    string identity = map.Identity(f, v, b); retryPlan = null; fe = f; vin = v; original = b; live = false; changes.Clear(); Populate();
                    status.Text = "OFFLINE — " + identity + ". Gravação bloqueada até nova leitura ao vivo.";
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Arquivo inválido"); }
            }
        }
        private void ImportValues()
        {
            using (var dialog = new OpenFileDialog { Filter = "Leitura/backup AIO (*.json)|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var snapshot = OfflineData.Load(dialog.FileName);
                    if (snapshot.Data["FE"] != fe || snapshot.Data["B0"] != vin) throw new InvalidOperationException("Importação exige o mesmo VIN e FE da leitura base.");
                    byte[] source = AioMap.Bytes(snapshot.Data["B1"]), current = AioMap.Bytes(original);
                    var imported = new Dictionary<string, ulong>();
                    foreach (var f in map.Fields.Where(f => f.Editable))
                        if (f.Read(source) != f.Read(current)) imported.Add(f.Name, f.Read(source));
                    map.Apply(current, imported); retryPlan = null; changes.Clear(); foreach (var c in imported) changes.Add(c.Key, c.Value);
                    Populate(); status.Text = changes.Count + " valor(es) importado(s) para revisão. Bits reservados não são importados. Nada gravado.";
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Importação rejeitada"); }
            }
        }
        private async Task Write()
        {
            if (!live || uncertain || session == null || !session.Ready) throw new InvalidOperationException("Faça uma leitura ao vivo na sessão atual.");
            var plan = new AioWritePlan { Fe = fe, Vin = vin, Original = original, Changes = new Dictionary<string, ulong>(changes), Proposed = AioMap.Hex(map.Apply(AioMap.Bytes(original), changes)) };
            plan.Validate(map);
            string fields = string.Join(Environment.NewLine, changes.Select(c => c.Key + ": " + map.Fields.Single(f => f.Name == c.Key).Read(AioMap.Bytes(original)) + " → " + c.Value).ToArray());
            using (var review = new AioReviewForm(fields + Environment.NewLine + Environment.NewLine + map.Diff(original, plan.Proposed)))
                if (review.ShowDialog(this) != DialogResult.OK) return;
            string path;
            using (var save = new SaveFileDialog { Filter = "Backup B1 original (*.json)|*.json", FileName = "aio-b1-antes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true })
            { if (save.ShowDialog(this) != DialogResult.OK) return; path = save.FileName; }
            retryPlan = plan; retryBackup = path;
            status.Text = "Conferindo dados atuais, salvando backup e gravando uma vez…";
            await PerformWrite(plan, path, true);
        }
        private async Task SendAgain()
        {
            if (retryPlan == null || session == null || !session.CanRetryLastWrite || uncertain) return;
            string path = Path.Combine(Path.GetDirectoryName(retryBackup), Path.GetFileNameWithoutExtension(retryBackup) + "-reenvio-" + Guid.NewGuid().ToString("N") + ".json");
            status.Text = "Enviando o comando de gravação preparado…";
            await PerformWrite(retryPlan, path, false);
        }
        private async Task PerformWrite(AioWritePlan plan, string path, bool checkCurrent)
        {
            try
            {
                await session.WriteB1Async(map, plan, path, CancellationToken.None, checkCurrent);
                retryPlan = null;
                original = plan.Proposed; changes.Clear(); live = false; Populate();
                status.Text = "B1 gravado e relido: 180 bytes conferidos. Faça nova leitura antes de outra gravação. Teste o efeito e a persistência na central.";
                MessageBox.Show(this, "Gravação aceita e releitura idêntica à proposta.\n\nBackup original: " + path + "\n\nFeche este editor e desconecte pelo aplicativo. Verifique o tema na central e depois de um desligamento normal. Não há reinício forçado automático.", "B1 conferido");
            }
            catch (Exception ex)
            {
                if (session.CanRetryLastWrite)
                    throw new InvalidOperationException("Gravação não enviada: " + ex.Message + "\n\nO comando preparado foi mantido. Clique em Reenviar gravação para enviá-lo diretamente, sem nova revisão nem leitura prévia. Nenhuma repetição automática.", ex);
                uncertain = true; live = false;
                throw new InvalidOperationException("Operação não concluída: " + ex.Message + "\n\nNão repita a gravação. Feche o editor, desconecte/reconecte e faça uma leitura para determinar o conteúdo atual. Backup, se criado: " + path, ex);
            }
        }
        public void LoadDemo(Snapshot snapshot)
        {
            fe = snapshot.Data["FE"]; vin = snapshot.Data["B0"]; original = snapshot.Data["B1"]; map.Identity(fe, vin, original);
            live = false; search.Text = "AMBIANCE"; Populate(); status.Text = "PRÉVIA OFFLINE — valores do arquivo; nenhuma porta serial aberta.";
        }
    }
    internal sealed class AioReviewForm : Form
    {
        public AioReviewForm(string diff)
        {
            Text = "Revisar gravação B1"; Size = new Size(860, 610); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Será enviado o bloco B1 completo (180 bytes), alterando apenas os campos abaixo.\nO aplicativo confere FE/VIN/B1 atuais antes de gravar, salva o original e relê o resultado.\nA interpretação do mapa e o efeito na sua AIO ainda são experimentais." }, 0, 0);
            layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), Text = diff }, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var yes = new Button { Text = "Salvar backup e gravar", Width = 210, Height = 34, DialogResult = DialogResult.OK };
            var no = new Button { Text = "Cancelar", Width = 115, Height = 34, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { yes, no }); layout.Controls.Add(buttons, 0, 2); CancelButton = no;
        }
    }
}
