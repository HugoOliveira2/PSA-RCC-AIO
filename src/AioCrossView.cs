using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AioAssist
{
    public sealed class AioCrossCandidate
    {
        public string Zone, Bits, Name, Source, Description, Type;
        public bool SameWidth, ByteAligned;
        public int Delta;
        public Dictionary<string, object> Choices;
        public Dictionary<string, object>[] Legacy;
        public bool LegacyConflict { get { return Legacy.Select(d => OfflineData.StringValue(d, "description")).Where(s => s != "").Distinct().Count() > 1; } }
        public string Descriptions()
        {
            var descriptions = Legacy.Select(d => OfflineData.StringValue(d, "description")).Where(s => s != "").Distinct().ToArray();
            return descriptions.Length > 0 ? string.Join(" / ", descriptions) : Description == Name ? "Sem descrição textual no NAC" : Description;
        }
    }
    public sealed class AioCrossEntry
    {
        public AioField Field;
        public string Kind;
        public bool MissingDescription;
        public readonly List<AioCrossCandidate> Candidates = new List<AioCrossCandidate>();
        public string LinkLabel
        {
            get
            {
                if (Kind == "none") return "Sem correspondente";
                if (Kind == "position") return "Candidato por posição";
                if (Kind == "ambiguous" || Candidates.Count != 1) return "Ambíguo";
                if (Candidates[0].LegacyConflict) return "Descrição NAC ambígua";
                return Candidates[0].SameWidth && Candidates[0].ByteAligned ? "Nome e layout relativo" : "Nome igual; layout difere";
            }
        }
    }
    public sealed class AioCrossData
    {
        public readonly AioMap Map = AioMap.Load();
        public readonly AioTranslations Translations = AioTranslations.Load();
        public readonly List<AioCrossEntry> Entries = new List<AioCrossEntry>();
        public static AioCrossData Load()
        {
            string json;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("aio-nac-cross.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) json = reader.ReadToEnd();
            var root = OfflineData.Object(OfflineData.Serializer().DeserializeObject(json)); var data = new AioCrossData();
            if (OfflineData.StringValue(root, "schema") != "aio-nac-cross-v1" || !(bool)root["readOnly"] ||
                OfflineData.StringValue(root, "aioSourceSha256") != data.Map.SourceHash) throw new FormatException("Cruzamento incompatível com o mapa AIO incorporado.");
            foreach (var item in (object[])root["entries"])
            {
                var d = OfflineData.Object(item); string name = OfflineData.StringValue(d, "aio_name");
                var entry = new AioCrossEntry { Field = data.Map.Fields.Single(f => f.Name == name), Kind = OfflineData.StringValue(d, "kind"), MissingDescription = (bool)d["lacks_aio_description"] };
                if (entry.Field.Bits != OfflineData.StringValue(d, "aio_bits")) throw new FormatException("Bits AIO divergentes no cruzamento.");
                foreach (var value in (object[])d["candidates"])
                {
                    var c = OfflineData.Object(value);
                    entry.Candidates.Add(new AioCrossCandidate { Name = OfflineData.StringValue(c, "name"), Zone = OfflineData.StringValue(c, "zone"),
                        Bits = OfflineData.StringValue(c, "bits"), Type = OfflineData.StringValue(c, "type"), Source = OfflineData.StringValue(c, "source"),
                        Description = OfflineData.StringValue(c, "description"), SameWidth = (bool)c["same_width"], ByteAligned = (bool)c["byte_aligned"],
                        Delta = Convert.ToInt32(c["delta_bits"]), Choices = OfflineData.Object(c["choices"]), Legacy = ((object[])c["legacy"]).Select(OfflineData.Object).ToArray() });
                }
                data.Entries.Add(entry);
            }
            if (data.Entries.Count != 292 || data.Entries.Select(e => e.Field.Name).Distinct().Count() != 292) throw new FormatException("Cruzamento incompleto/duplicado.");
            return data;
        }
        public string Description(AioCrossEntry entry)
        { return entry.Candidates.Count == 0 ? "Sem descrição NAC correspondente" : string.Join(" / ", entry.Candidates.Select(c => TranslateDescription(c.Descriptions())).Distinct().ToArray()); }
        public string AioValue(AioCrossEntry entry, byte[] payload)
        { ulong raw = entry.Field.Read(payload); return raw + " — " + Translations.Value(entry.Field, raw); }
        public string Hypothesis(AioCrossEntry entry, byte[] payload)
        {
            if (entry.Candidates.Count != 1) return "Sem interpretação única";
            var c = entry.Candidates[0]; if (!c.SameWidth) return "Não interpretar: largura diferente no NAC";
            if (c.LegacyConflict) return "Descrições NAC divergentes para esta posição; não há interpretação única";
            string raw = entry.Field.Read(payload).ToString(CultureInfo.InvariantCulture); var descriptions = new List<string>();
            if (c.Choices.ContainsKey(raw)) descriptions.Add(Convert.ToString(c.Choices[raw]));
            foreach (var legacy in c.Legacy)
            {
                var choices = OfflineData.Object(legacy["choices"]);
                if (choices.ContainsKey(raw)) descriptions.Add(Convert.ToString(choices[raw]));
            }
            var result = descriptions.Distinct().Select(TranslateOption).ToArray();
            return result.Length == 0 ? "Sem rótulo NAC para este valor; manter número bruto" : "Hipótese NAC: " + string.Join(" / ", result);
        }
        public static string TranslateDescription(string description)
        {
            string d = Regex.Replace(description ?? "", @"\s+", " ").Trim();
            var m = Regex.Match(d, @"^Vehicule menu (\d+) Item (\d+)$");
            if (m.Success) return "Menu do veículo " + m.Groups[1].Value + ", item " + m.Groups[2].Value;
            m = Regex.Match(d, @"^Vehicle Menu (\d+)$"); if (m.Success) return "Menu do veículo " + m.Groups[1].Value;
            m = Regex.Match(d, @"^Ambiance Menu (\d+)$"); if (m.Success) return "Opção de tema " + m.Groups[1].Value + " no menu";
            m = Regex.Match(d, @"^Battery level bargraph threshold (\d+)$"); if (m.Success) return "Limiar " + m.Groups[1].Value + " do indicador de bateria";
            if (d.StartsWith("Physical Button \"", StringComparison.OrdinalIgnoreCase)) return "Botão físico: " + TranslateFeature(d.Substring(17).Trim('"'));
            if (d.StartsWith("Touch button Position", StringComparison.OrdinalIgnoreCase)) return "Posição do botão na tela: " + TranslateFeature(d.Substring(21).Trim(' ', '"'));
            if (d.StartsWith("Display Touch button", StringComparison.OrdinalIgnoreCase)) return "Exibição do botão na tela: " + TranslateFeature(d.Substring(20).Trim(' ', '"'));
            string translated;
            return descriptionPt.TryGetValue(d, out translated) ? translated : d + (d.StartsWith("Sem descrição") ? "" : " (descrição original NAC)");
        }
        public static string TranslateOption(string option)
        {
            string value;
            if (optionPt.TryGetValue(option, out value)) return value;
            if (Regex.IsMatch(option, @"^(UTC|0x|Gen[123]|Law |Routing |BSI$|BTEL$|CAN-BUS$|LVDS|Arkamys|Peugeot$|Citro[eë]n$|DS$|Toyota$|Opel$|Holden$|Vauxhall$)")) return option;
            return option + " (opção original NAC)";
        }
        private static string TranslateFeature(string feature)
        {
            string value;
            return descriptionPt.TryGetValue(feature, out value) ? value : feature + " (original NAC)";
        }
        private static readonly Dictionary<string, string> descriptionPt = new Dictionary<string, string> {
            { "Default Ambiance", "Tema padrão" }, { "Default theme", "Tema padrão" }, { "Vehicle model", "Modelo do veículo (rótulo NAC; hipótese para a AIO)" },
            { "Automatic control of the volume \"ACV", "Controle automático de volume ACV" },
            { "Adaptive Cruise Control", "Controle de cruzeiro adaptativo" }, { "Blind spot monitoring", "Monitoramento de ponto cego" },
            { "Distance Alert (ARTIV)", "Alerta de distância ARTIV" }, { "Lane Guard system", "Sistema de manutenção em faixa" },
            { "Lane departure warning system (AFIL)", "Alerta de saída de faixa AFIL" }, { "Main beams assistant", "Assistente de farol alto" },
            { "Parking space measurement system", "Sistema de medição de vaga" }, { "Stop and Start", "Stop & Start" },
            { "Thermal preconditioning (Webasto)", "Pré-condicionamento térmico Webasto" }, { "Traction Control", "Controle de tração" },
            { "Parking assistance", "Assistência ao estacionamento" }, { "Semi-automatic parking system", "Estacionamento semiautomático" },
            { "On-board computer alerts (Vehicle without matrix)", "Alertas do computador de bordo (veículo sem painel matricial)" },
            { "Configuration of the on-board computer", "Configuração do computador de bordo" }, { "History of on-board computer alerts", "Histórico de alertas do computador de bordo" },
            { "On-board computer display", "Exibição do computador de bordo" }, { "Permanent display of the on-board computer", "Exibição permanente do computador de bordo" },
            { "Partial mileage display", "Exibição da quilometragem parcial" }, { "Time Zone", "Fuso horário" }, { "Time zone initialization", "Inicialização do fuso horário" },
            { "Initial date format", "Formato inicial de data" }, { "Date synchronization", "Sincronização da data" }, { "Initial date synchronization", "Sincronização inicial da data" },
            { "Date and time configuration", "Configuração de data e hora" }, { "Date and time display", "Exibição de data e hora" },
            { "Origin of the DSP coloring order", "Origem do comando de coloração sonora DSP" }, { "Dynamic Sport Pack", "Dynamic Sport Pack" },
            { "Dynamic Sport Pack Type", "Tipo do Dynamic Sport Pack" }, { "Audio informations export type on the CAN-BUS", "Tipo de transmissão das informações de áudio no CAN" },
            { "Media informations export type", "Tipo de transmissão das informações de mídia" }, { "Central Speaker", "Alto-falante central" },
            { "Amplifier powered by CD Charger", "Alimentação do amplificador pela disqueteira de CDs (rótulo NAC)" },
            { "DSP coloring sound efficiency", "Intensidade da coloração sonora DSP" }, { "DX Law for DSP sound", "Curva DX do som DSP" },
            { "Android Auto", "Android Auto" }, { "Autonomous telematic box (BTA)", "Unidade telemática autônoma BTA" },
            { "Air Conditioning Menu", "Menu de climatização" }, { "Air conditioning information at the top of the screen", "Informações de climatização no topo da tela" },
            { "MONO button type", "Tipo do botão MONO" }, { "Ionisor function", "Função de ionizador" }, { "Ambiance Perfume", "Perfume de ambiente" },
            { "Sound adjustment", "Ajuste de som" }, { "Radio export type", "Tipo de transmissão dos dados do rádio" },
            { "Number of satellites required to activate GPS positioning information", "Satélites necessários para ativar a informação de posição GPS" },
            { "Maximum speed of the cruise control", "Velocidade máxima do controle de cruzeiro" }, { "Minimum speed of the cruise control", "Velocidade mínima do controle de cruzeiro" },
            { "Maximum speed of the speed limiter", "Velocidade máxima do limitador" }, { "Minimum speed of the speed limiter", "Velocidade mínima do limitador" },
            { "Memorized speeds", "Velocidades memorizadas" }, { "Selected speeds", "Velocidades selecionadas" },
            { "Stop and Start function", "Função Stop & Start" }, { "Remote control", "Controle remoto" },
            { "High hysteresis of hybrid consumption", "Limite superior da histerese de consumo híbrido" }, { "Low hysteresis of hybrid consumption", "Limite inferior da histerese de consumo híbrido" },
            { "Battery bargraph threshold", "Limiar do indicador de bateria" }, { "Video display resolution 1", "Resolução de vídeo 1" }, { "Video display resolution 2", "Resolução de vídeo 2" },
            { "Car speakers for assistance call (BCALL)", "Alto-falantes do carro para chamada de assistência BCALL" }, { "Car speakers for information call (ICALL)", "Alto-falantes do carro para chamada de informações ICALL" },
            { "Web portal configuration", "Configuração do portal web" }, { "Speed limitation from Multifunction Camera in the windshield", "Limite de velocidade recebido da câmera multifunção do para-brisa" },
            { "Sound attenuation based on speed (km/h)", "Atenuação sonora em função da velocidade (km/h)" },
            { "Driver Restriction function", "Função de restrição do condutor" }, { "Advanced Shutdown (Shutdown when the door is opened: information and sensor required !)", "Desligamento ao abrir a porta (requer informação e sensor correspondentes)" }
        };
        private static readonly Dictionary<string, string> optionPt = new Dictionary<string, string> {
            { "Absent", "Ausente" }, { "Present", "Presente" }, { "Disabled", "Desativado" }, { "Activated", "Ativado" },
            { "Absent Function", "Função ausente" }, { "Absent function", "Função ausente" }, { "No button", "Sem botão" },
            { "Touch Button", "Botão na tela" }, { "Physical button", "Botão físico" },
            { "No air conditioning", "Sem ar-condicionado" }, { "Manual air conditioning", "Ar-condicionado manual" },
            { "Dual-Zone Automatic Air Conditioning", "Ar-condicionado automático de duas zonas" }, { "Bi-zone", "Duas zonas" },
            { "Safety", "Segurança" }, { "Comfort", "Conforto" }, { "Vehicle access / Parking", "Acesso ao veículo / estacionamento" },
            { "Instant consumption", "Consumo instantâneo" }, { "N/A", "Não aplicável / não informado" }
        };
    }

    // Research overlay only. Does not reference Session or expose editing/import actions.
    public sealed class AioCrossView : Form
    {
        private readonly AioCrossData cross = AioCrossData.Load();
        private readonly Snapshot snapshot;
        private readonly byte[] payload;
        private readonly DataGridView grid = new DataGridView();
        private readonly ComboBox filter = new ComboBox();
        private readonly TextBox search = new TextBox(), detail = new TextBox();
        private readonly Label summary = new Label();
        public AioCrossView(Snapshot source)
        {
            var validation = new AioOfflineModel(); validation.SetCurrent(source); snapshot = source; payload = AioMap.Bytes(snapshot.Data["B1"]);
            Text = "AIO Assist 0.6.1 — Cruzamento AIO × NAC (somente consulta)"; Size = new Size(1350, 870); MinimumSize = new Size(1050, 720);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); Controls.Add(root);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Arquivo: " + Path.GetFileName(snapshot.Name) + " | " + snapshot.Summary + Environment.NewLine +
                "258 vínculos por nome • 29 candidatos por posição • 5 sem correspondente. Descrições e opções transferidas do NAC são hipóteses para AIO." + Environment.NewLine +
                "Valores abaixo são extraídos do B1 da leitura AIO. Não são uma leitura de uma central NAC." }, 0, 0);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            filter.DropDownStyle = ComboBoxStyle.DropDownList; filter.Width = 275;
            filter.Items.AddRange(new object[] { "Sem descrição original AIO", "Todos", "Nome e layout relativo", "Nome igual; layout difere", "Candidato por posição", "Descrição NAC ambígua", "Sem correspondente" }); filter.SelectedIndex = 0;
            search.Width = 420; bar.Controls.Add(filter); bar.Controls.Add(new Label { Text = "Buscar:", AutoSize = true, Padding = new Padding(8, 4, 0, 0) }); bar.Controls.Add(search);
            root.Controls.Add(bar, 0, 1);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (var title in new[] { "Identificador AIO", "Valor AIO", "Descrição NAC (hipótese)", "Zona NAC", "AIO bits ← NAC bits", "Tipo de vínculo" }) grid.Columns.Add(title, title);
            int[] weights = { 125, 38, 150, 39, 80, 95 }; for (int i = 0; i < weights.Length; i++) grid.Columns[i].FillWeight = weights[i];
            grid.BackgroundColor = Color.White; root.Controls.Add(grid, 0, 2);
            detail.Multiline = true; detail.ReadOnly = true; detail.ScrollBars = ScrollBars.Vertical; detail.Dock = DockStyle.Fill; root.Controls.Add(detail, 0, 3);
            summary.Dock = DockStyle.Fill; root.Controls.Add(summary, 0, 4);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Somente pesquisa. Esta tela não altera o mapa de gravação. NAC AMBIANCE_CURRENT não tem equivalente confirmado na AIO: a região correspondente é reservada no mapa AIO." }, 0, 5);
            filter.SelectedIndexChanged += delegate { Populate(); }; search.TextChanged += delegate { Populate(); }; grid.SelectionChanged += delegate { Detail(); };
            Populate();
        }
        private void Populate()
        {
            string selected = Convert.ToString(filter.SelectedItem); grid.Rows.Clear(); int shown = 0;
            foreach (var e in cross.Entries)
            {
                string description = cross.Description(e);
                if (selected == "Sem descrição original AIO" ? !e.MissingDescription : selected != "Todos" && selected != e.LinkLabel) continue;
                if ((e.Field.Name + " " + description + " " + string.Join(" ", e.Candidates.Select(c => c.Name + " " + c.Zone).ToArray())).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string zones = string.Join(" / ", e.Candidates.Select(c => c.Zone).Distinct().ToArray());
                string positions = e.Field.Bits + " ← " + string.Join(" / ", e.Candidates.Select(c => c.Bits).Distinct().ToArray());
                int row = grid.Rows.Add(e.Field.Name, e.Field.Read(payload), description, zones, positions, e.LinkLabel); grid.Rows[row].Tag = e;
                if (e.Kind == "position") grid.Rows[row].DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
                else if (e.Kind == "exact" && (e.LinkLabel.Contains("difere") || e.LinkLabel.Contains("ambígua"))) grid.Rows[row].DefaultCellStyle.BackColor = Color.MistyRose;
                shown++;
            }
            summary.Text = shown + " / 292 campos exibidos. 188 sem descrição textual AIO; 166 deles têm descrição localizada no nac.json por meio do cruzamento.\nBytes começam em 1 dentro dos dados da zona. Nenhum campo reservado foi promovido a parâmetro.";
            Detail();
        }
        private void Detail()
        {
            var e = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as AioCrossEntry;
            if (e == null) { detail.Clear(); return; }
            var text = new StringBuilder();
            text.AppendLine("AIO: " + e.Field.Name + " | B1 " + e.Field.Bits + " | " + cross.Translations.Name(e.Field));
            text.AppendLine("Valor da leitura AIO: " + cross.AioValue(e, payload));
            text.AppendLine("Vínculo: " + e.LinkLabel + ". Coincidência de nome/posição não comprova comportamento no firmware AIO.");
            foreach (var c in e.Candidates)
            {
                text.AppendLine("NAC: " + c.Zone + " | " + c.Name + " | " + c.Bits + " | diferença de posição: " + c.Delta + " bits");
                text.AppendLine("Descrição original NAC: " + c.Descriptions());
                text.AppendLine("Fonte: https://github.com/prototux/PSA-RE/blob/master/" + c.Source);
                foreach (var legacy in c.Legacy)
                {
                    text.AppendLine("nac.json: " + OfflineData.StringValue(legacy, "code") + " / " + OfflineData.StringValue(legacy, "name") + " | pos " + OfflineData.StringValue(legacy, "pos") + ", máscara " + OfflineData.StringValue(legacy, "mask"));
                    var choices = OfflineData.Object(legacy["choices"]);
                    if (choices.Count > 0) text.AppendLine("Opções NAC (valores normalizados pela máscara): " + string.Join(" | ", choices.Select(c2 => c2.Key + " = " + AioCrossData.TranslateOption(Convert.ToString(c2.Value))).ToArray()));
                }
            }
            text.AppendLine(cross.Hypothesis(e, payload)); detail.Text = text.ToString();
        }
        public void SetSearch(string text) { search.Text = text; }
        public void ShowAll() { filter.SelectedItem = "Todos"; }
    }
}
