using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AioAssist
{
    public sealed class Snapshot
    {
        public string Name = "";
        public string Kind = "";
        public string Protocol = "";
        public string Summary = "";
        public string Hash = "";
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Status = new Dictionary<string, string>();
        public readonly List<CalRecord> Records = new List<CalRecord>();
    }
    public sealed class CalRecord
    {
        public int Line;
        public string Type;
        public string Address;
        public string Data;
    }
    public sealed class FieldMap
    {
        public string Zone, ZoneName, Name, Description;
        public int Position;
        public byte Mask;
        public readonly Dictionary<byte, string> Choices = new Dictionary<byte, string>();
    }
    public sealed class RepositoryMap
    {
        public string Path = "";
        public int ZoneCount;
        public readonly Dictionary<string, string> ZoneNames = new Dictionary<string, string>();
        public readonly List<FieldMap> Fields = new List<FieldMap>();
    }
    public sealed class ByteDiff
    {
        public int Offset;
        public string Left, Right, Xor, Bits;
        public bool Equal;
    }
    public sealed class RecordDiff
    {
        public string Key, Left, Right, State;
    }
    public static class OfflineData
    {
        public static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 16777216, RecursionLimit = 80 }; }
        public static Dictionary<string, object> Object(object value)
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new FormatException("Objeto JSON inválido.");
            return result;
        }
        public static string StringValue(Dictionary<string, object> value, string name)
        { return value.ContainsKey(name) && value[name] != null ? Convert.ToString(value[name], CultureInfo.InvariantCulture) : ""; }
        public static string Hex(string value)
        {
            string result = Regex.Replace(value ?? "", "\\s", "").ToUpperInvariant();
            if (result.Length % 2 != 0 || !Regex.IsMatch(result, "^[0-9A-F]*$")) throw new FormatException("Dados hexadecimais inválidos.");
            return result;
        }
        public static string HashFile(string path)
        { using (var input = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""); }
        public static Snapshot Load(string path)
        {
            if (new FileInfo(path).Length > 8388608) throw new FormatException("Arquivo acima do limite de 8 MB.");
            Snapshot result = Parse(File.ReadAllText(path), System.IO.Path.GetExtension(path));
            result.Name = System.IO.Path.GetFileName(path); result.Hash = HashFile(path); return result;
        }
        private static void AddZone(Snapshot result, string zone, string hex, string status)
        {
            zone = zone.ToUpperInvariant();
            if (!Regex.IsMatch(zone, "^(?:[0-9A-F]{2}|[0-9A-F]{4})$")) throw new FormatException("ID de zona inválido: " + zone);
            if (result.Status.ContainsKey(zone)) throw new FormatException("Zona duplicada: " + zone);
            result.Status.Add(zone, status);
            if (hex != "") result.Data.Add(zone, Hex(hex));
        }
        public static Snapshot Parse(string text, string extension)
        {
            var result = new Snapshot();
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var root = Object(Serializer().DeserializeObject(text));
                if (StringValue(root, "schema") != "aio-assist-diagnostic-v1" || !root.ContainsKey("zones"))
                    throw new FormatException("Selecione uma exportação AIO Assist. Para nac.json, use Carregar mapa.");
                result.Kind = "Leitura AIO JSON"; result.Protocol = StringValue(root, "protocol");
                result.Summary = "Exportação parcial em " + StringValue(root, "capturedAt") + "; versão " + StringValue(root, "applicationVersion");
                foreach (object item in (IEnumerable)root["zones"])
                {
                    var zone = Object(item); string id = StringValue(zone, "Zone").ToUpperInvariant();
                    string hex = Hex(StringValue(zone, "Hex")); string response = StringValue(zone, "Response").ToUpperInvariant();
                    if (hex != "" && response != "61" + id + hex)
                        throw new FormatException("Resposta e conteúdo discordam na zona " + id + ".");
                    AddZone(result, id, hex, StringValue(zone, "Status"));
                }
            }
            else if (extension.Equals(".nac", StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = "Configuração NAC textual";
                foreach (string raw in text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim(); if (line.StartsWith("#") || line.StartsWith("//")) continue;
                    int separator = line.IndexOf('=');
                    if (separator < 0) throw new FormatException("Linha .nac inválida. Esperado ZONA=HEX.");
                    string hex = Hex(line.Substring(separator + 1));
                    AddZone(result, line.Substring(0, separator).Trim(), hex, hex == "" ? "Sem dados" : "Presente no arquivo");
                }
                result.Protocol = result.Status.Keys.All(z => z.Length == 4) ? "Zonas de 2 bytes; mapa NAC" : "IDs de zona conforme arquivo";
                result.Summary = "Dados .nac interpretados como valores por zona; nenhuma gravação disponível.";
            }
            else if (extension.Equals(".cal", StringComparison.OrdinalIgnoreCase))
            {
                result.Kind = "Calibração S-record"; result.Protocol = "Registros de calibração; não é backup de zonas";
                int index = 0;
                foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    index++;
                    string line = raw.Trim().ToUpperInvariant();
                    if (!Regex.IsMatch(line, "^S[012378][0-9A-F]+$")) throw new FormatException("Registro CAL inválido na linha " + index);
                    string bytes = Hex(line.Substring(2));
                    int count = Convert.ToInt32(bytes.Substring(0, 2), 16);
                    if (bytes.Length / 2 != count + 1) throw new FormatException("Comprimento S-record inválido na linha " + index);
                    int sum = 0; for (int i = 0; i < bytes.Length; i += 2) sum += Convert.ToInt32(bytes.Substring(i, 2), 16);
                    if ((sum & 255) != 255) throw new FormatException("Checksum S-record inválido na linha " + index);
                    string type = line.Substring(0, 2);
                    int addressLength = type == "S2" || type == "S8" ? 3 : type == "S3" || type == "S7" ? 4 : 2;
                    // The project's S801FE is a short terminator, not a data record.
                    if (type == "S8" && count == 1) addressLength = 0;
                    if (count < addressLength + 1) throw new FormatException("Endereço S-record ausente na linha " + index);
                    result.Records.Add(new CalRecord { Line = index, Type = type,
                        Address = bytes.Substring(2, addressLength * 2),
                        Data = bytes.Substring(2 + addressLength * 2, (count - addressLength - 1) * 2) });
                }
                var zi = result.Records.FirstOrDefault(r => r.Type == "S1");
                result.Summary = result.Records.Count + " registros; checksums S-record conferidos. ";
                if (zi != null && zi.Data.Length >= 10)
                    result.Summary += "Cabeçalho ZI bruto: " + zi.Data + ". ";
                result.Summary += "Endereços S2/S3 não são IDs de zona B1/2106; o mapa não é aplicado a esses endereços.";
            }
            else throw new FormatException("Formato não suportado. Abra .json AIO Assist, .nac textual ou .cal.");
            return result;
        }
        private static string Language(object value)
        {
            var translations = value as Dictionary<string, object>;
            if (translations == null) return value == null ? "" : Convert.ToString(value);
            foreach (string language in new[] { "pt", "en", "fr" })
                if (translations.ContainsKey(language)) return Convert.ToString(translations[language]);
            return "";
        }
        public static RepositoryMap LoadMap(string path)
        {
            if (new FileInfo(path).Length > 8388608) throw new FormatException("Mapa acima do limite de 8 MB.");
            RepositoryMap result = ParseMap(File.ReadAllText(path)); result.Path = path; return result;
        }
        public static RepositoryMap ParseMap(string text)
        {
            var root = Object(Serializer().DeserializeObject(text));
            if (!root.ContainsKey("NAC")) throw new FormatException("Esperado mapa nac.json com raiz NAC. Outros layouts não são aplicados automaticamente.");
            var nac = Object(root["NAC"]);
            var zones = Object(nac["zones"]);
            var map = new RepositoryMap { ZoneCount = zones.Count };
            foreach (var item in zones)
            {
                string id = item.Key.Split(';')[0].ToUpperInvariant(); var zone = Object(item.Value);
                if (!Regex.IsMatch(id, "^[0-9A-F]{4}$")) throw new FormatException("Este leitor de mapa NAC espera zonas de 2 bytes: " + id);
                string zoneName = StringValue(zone, "name"); map.ZoneNames[id] = zoneName;
                if (!zone.ContainsKey("params")) continue;
                foreach (object param in (IEnumerable)zone["params"])
                {
                    var data = Object(param); int pos;
                    if (!int.TryParse(StringValue(data, "pos"), NumberStyles.Integer, CultureInfo.InvariantCulture, out pos) || pos < 4)
                        continue;
                    string mask = StringValue(data, "mask");
                    if (!Regex.IsMatch(mask, "^[0-9A-Fa-f]{2}$")) continue;
                    var field = new FieldMap { Zone = id, ZoneName = zoneName,
                        Name = StringValue(data, "name") + " " + StringValue(data, "extra_name"),
                        Description = data.ContainsKey("detail") ? Language(data["detail"]) : "",
                        Position = pos, Mask = Convert.ToByte(mask, 16) };
                    if (data.ContainsKey("listbox"))
                        foreach (object choice in (IEnumerable)data["listbox"])
                        {
                            var option = Object(choice); string value = StringValue(option, "value");
                            if (Regex.IsMatch(value, "^[0-9A-Fa-f]{2}$"))
                                field.Choices[Convert.ToByte(value, 16)] = option.ContainsKey("text") ? Language(option["text"]) : "";
                        }
                    map.Fields.Add(field);
                }
            }
            return map;
        }
        public static string Decode(FieldMap field, Snapshot snapshot)
        {
            if (snapshot == null || !snapshot.Data.ContainsKey(field.Zone)) return "Zona não disponível";
            string hex = snapshot.Data[field.Zone]; int offset = field.Position - 4;
            if (offset >= hex.Length / 2) return "Posição além dos dados";
            byte raw = Convert.ToByte(hex.Substring(offset * 2, 2), 16); byte masked = (byte)(raw & field.Mask);
            if (field.Choices.Count == 0) return "Byte 0x" + raw.ToString("X2") + "; campo não decodificado";
            return field.Choices.ContainsKey(masked) ? field.Choices[masked] + " [0x" + masked.ToString("X2") + "]" :
                "Valor 0x" + masked.ToString("X2") + " sem opção no mapa";
        }
        public static string ZoneState(Snapshot left, Snapshot right, string zone)
        {
            bool a = left != null && left.Data.ContainsKey(zone), b = right != null && right.Data.ContainsKey(zone);
            if (right == null) return a ? "Dados disponíveis" : "Consulta sem dados";
            if (!a && !b) return "Sem dados nos dois arquivos";
            if (!a) return "Somente na referência";
            if (!b) return "Somente na base";
            return left.Data[zone] == right.Data[zone] ? "Idêntica" : "Diferente";
        }
        public static List<ByteDiff> Diff(string left, string right)
        {
            left = Hex(left); right = Hex(right); var rows = new List<ByteDiff>();
            for (int i = 0; i < Math.Max(left.Length, right.Length) / 2; i++)
            {
                string a = i * 2 < left.Length ? left.Substring(i * 2, 2) : "—";
                string b = i * 2 < right.Length ? right.Substring(i * 2, 2) : "—";
                int xor = a == "—" || b == "—" ? -1 : Convert.ToByte(a, 16) ^ Convert.ToByte(b, 16);
                rows.Add(new ByteDiff { Offset = i, Left = a, Right = b, Equal = a == b,
                    Xor = xor < 0 ? "—" : xor.ToString("X2"), Bits = xor < 0 ? "Dado ausente" : Convert.ToString(xor, 2).PadLeft(8, '0') });
            }
            return rows;
        }
        private static Dictionary<string, string> CalIndex(Snapshot file)
        {
            var result = new Dictionary<string, string>(); var occurrences = new Dictionary<string, int>();
            if (file == null) return result;
            foreach (var record in file.Records)
            {
                string address = record.Type + "@" + record.Address;
                if (!occurrences.ContainsKey(address)) occurrences[address] = 0;
                occurrences[address]++;
                result[address + "#" + occurrences[address]] = record.Data;
            }
            return result;
        }
        public static List<RecordDiff> CompareRecords(Snapshot left, Snapshot right)
        {
            var a = CalIndex(left); var b = CalIndex(right); var ids = new SortedSet<string>(a.Keys); ids.UnionWith(b.Keys);
            return ids.Select(id => new RecordDiff { Key = id,
                Left = a.ContainsKey(id) ? a[id] : "—", Right = b.ContainsKey(id) ? b[id] : "—",
                State = !a.ContainsKey(id) ? "Somente na referência" : !b.ContainsKey(id) ? "Somente na base" : a[id] == b[id] ? "Idêntico" : "Diferente" }).ToList();
        }
        public static string Report(Snapshot left, Snapshot right, RepositoryMap map)
        {
            var text = new StringBuilder(); text.AppendLine("AIO Assist — comparação offline sem gravação");
            foreach (var item in new[] { new { Name = "BASE", Value = left }, new { Name = "REFERÊNCIA", Value = right } })
                if (item.Value != null)
                {
                    text.AppendLine(item.Name + ": " + item.Value.Name + " | " + item.Value.Kind + " | " + item.Value.Protocol);
                    text.AppendLine("SHA256: " + item.Value.Hash); text.AppendLine(item.Value.Summary);
                }
            if (map != null)
                text.AppendLine("Mapa NAC: " + map.ZoneCount + " zonas, " + map.Fields.Count + " campos. Posição do mapa inclui 4 bytes; deslocamento do dado = posição - 4.");
            var ids = new SortedSet<string>();
            if (left != null) ids.UnionWith(left.Status.Keys); if (right != null) ids.UnionWith(right.Status.Keys);
            text.AppendLine();
            foreach (string zone in ids)
            {
                string a = left != null && left.Data.ContainsKey(zone) ? left.Data[zone] : "";
                string b = right != null && right.Data.ContainsKey(zone) ? right.Data[zone] : "";
                text.AppendLine("Zona " + zone + ": " + ZoneState(left, right, zone) + "; base " + a.Length / 2 + " bytes; referência " + b.Length / 2 + " bytes; " +
                    (map != null && map.ZoneNames.ContainsKey(zone) ? map.ZoneNames[zone] : "SEM MAPA PARA ESTE ID"));
                if (left != null && left.Status.ContainsKey(zone)) text.AppendLine("  Resultado na base: " + left.Status[zone]);
                if (right != null && right.Status.ContainsKey(zone)) text.AppendLine("  Resultado na referência: " + right.Status[zone]);
                if (a != "" && b != "")
                {
                    var changes = Diff(a, b).Where(row => !row.Equal).ToList();
                    text.AppendLine("  Diferenças: " + changes.Count);
                    foreach (var row in changes) text.AppendLine("  Offset 0x" + row.Offset.ToString("X4") + ": " + row.Left + " -> " + row.Right + "; XOR " + row.Xor + "; bits " + row.Bits);
                }
            }
            if (left != null && right != null && !left.Data.Keys.Intersect(right.Data.Keys).Any())
                text.AppendLine("Nenhuma zona com dados tem o mesmo ID nos dois arquivos. Não é possível inferir diferenças de B1 por outro ID ou endereço CAL.");
            var recordChanges = CompareRecords(left, right);
            if (recordChanges.Count > 0)
            {
                text.AppendLine(); text.AppendLine("REGISTROS CAL POR TIPO ENDEREÇO E OCORRÊNCIA — SEM EQUIVALÊNCIA FUNCIONAL INFERIDA");
                foreach (var record in recordChanges)
                    text.AppendLine(record.Key + " | " + record.State + " | base: " + record.Left + " | referência: " + record.Right);
            }
            if (map != null)
            {
                text.AppendLine(); text.AppendLine("CAMPOS DO MAPA COM DADOS NO MESMO ID");
                foreach (FieldMap field in map.Fields.Where(f => (left != null && left.Data.ContainsKey(f.Zone)) || (right != null && right.Data.ContainsKey(f.Zone))))
                    text.AppendLine(field.Zone + " | " + field.Name + " | " + field.Description + " | posição " + field.Position + " | máscara " + field.Mask.ToString("X2") + " | " + Decode(field, left) + " | " + Decode(field, right));
            }
            text.AppendLine(); text.AppendLine("Correspondência por ID não comprova compatibilidade entre hardware e firmware. B1 não é renomeada como 2106 ou 2145.");
            text.AppendLine("CAL é exibido por registros e cabeçalhos, sem atribuir endereços S-record a zonas de configuração. Checksums conferidos não comprovam compatibilidade.");
            return text.ToString();
        }
    }

    public sealed class OfflineForm : Form
    {
        private Snapshot left, right;
        private RepositoryMap map;
        private readonly DataGridView zones = NewGrid();
        private readonly DataGridView bytes = NewGrid();
        private readonly DataGridView fields = NewGrid();
        private readonly DataGridView records = NewGrid();
        private readonly TextBox notes = new TextBox();
        private readonly Label labels = new Label();
        private readonly Label overview = new Label();
        private readonly TextBox filter = new TextBox();
        private readonly CheckBox changesOnly = new CheckBox();
        public OfflineForm(Snapshot initial)
        {
            left = initial; Text = "AIO Assist 0.3 | Leitor e comparador offline";
            Size = new Size(1240, 900); MinimumSize = new Size(1020, 720); Font = new Font("Segoe UI", 10);
            BackColor = Color.White; StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(15) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 61)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "Arquivos e mapa de parâmetros", Font = new Font("Segoe UI", 17, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddButton(toolbar, "Abrir base", 125, () => OpenFile(true));
            AddButton(toolbar, "Abrir referência", 155, () => OpenFile(false));
            AddButton(toolbar, "Carregar mapa", 145, OpenMap);
            AddButton(toolbar, "Exportar comparação", 205, Export);
            root.Controls.Add(toolbar, 0, 1);
            labels.Dock = DockStyle.Fill; labels.AutoEllipsis = true; root.Controls.Add(labels, 0, 2);
            zones.Columns.Add("zone", "Zona"); zones.Columns.Add("name", "Nome no mapa"); zones.Columns.Add("left", "Bytes base"); zones.Columns.Add("right", "Bytes referência"); zones.Columns.Add("state", "Comparação");
            zones.Columns[0].FillWeight = 15; zones.Columns[1].FillWeight = 70; zones.Columns[2].FillWeight = 20; zones.Columns[3].FillWeight = 25; zones.Columns[4].FillWeight = 65;
            root.Controls.Add(zones, 0, 3);
            overview.Dock = DockStyle.Fill; overview.ForeColor = Color.FromArgb(60, 60, 60); overview.Padding = new Padding(0, 7, 0, 0);
            root.Controls.Add(overview, 0, 4);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            root.Controls.Add(tabs, 0, 5);
            var byteTab = new TabPage("Bytes da zona selecionada"); var bytePanel = new Panel { Dock = DockStyle.Fill };
            changesOnly.Text = "Somente diferenças"; changesOnly.Dock = DockStyle.Top; changesOnly.Height = 30;
            bytes.Columns.Add("offset", "Offset decimal / hex"); bytes.Columns.Add("left", "Base"); bytes.Columns.Add("right", "Referência"); bytes.Columns.Add("xor", "XOR"); bytes.Columns.Add("bits", "Bits diferentes");
            bytePanel.Controls.Add(bytes); bytePanel.Controls.Add(changesOnly); byteTab.Controls.Add(bytePanel); tabs.TabPages.Add(byteTab);
            var fieldTab = new TabPage("Mapa do repositório"); var fieldPanel = new Panel { Dock = DockStyle.Fill };
            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35 };
            searchPanel.Controls.Add(new Label { Text = "Buscar zona ou parâmetro:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            filter.Width = 300; searchPanel.Controls.Add(filter);
            foreach (string name in new[] { "Zona", "Parâmetro", "Descrição", "Posição / máscara", "Base", "Referência" }) fields.Columns.Add(name, name);
            fields.Columns[0].FillWeight = 14; fields.Columns[1].FillWeight = 50; fields.Columns[2].FillWeight = 90; fields.Columns[3].FillWeight = 38; fields.Columns[4].FillWeight = 65; fields.Columns[5].FillWeight = 65;
            fieldPanel.Controls.Add(fields); fieldPanel.Controls.Add(searchPanel); fieldTab.Controls.Add(fieldPanel); tabs.TabPages.Add(fieldTab);
            var calTab = new TabPage("Registros CAL");
            foreach (string name in new[] { "Tipo @ endereço # ocorrência", "Comparação", "Dados base", "Dados referência" }) records.Columns.Add(name, name);
            records.Columns[0].FillWeight = 45; records.Columns[1].FillWeight = 40; records.Columns[2].FillWeight = 90; records.Columns[3].FillWeight = 90;
            calTab.Controls.Add(records); tabs.TabPages.Add(calTab);
            var notesTab = new TabPage("Resumo e limites"); notes.Dock = DockStyle.Fill; notes.Multiline = true; notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Both; notes.WordWrap = false; notes.Font = new Font("Consolas", 9); notesTab.Controls.Add(notes); tabs.TabPages.Add(notesTab);
            zones.SelectionChanged += delegate { RefreshBytes(); };
            changesOnly.CheckedChanged += delegate { RefreshBytes(); };
            filter.TextChanged += delegate { RefreshFields(); };
            string bundledMap = System.IO.Path.Combine(Application.StartupPath, "references", "nac.json");
            if (File.Exists(bundledMap))
                try { map = OfflineData.LoadMap(bundledMap); } catch (Exception ex) { MessageBox.Show(this, "Não foi possível carregar mapa: " + ex.Message); }
            RefreshAll();
        }
        private static DataGridView NewGrid()
        {
            return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BorderStyle = BorderStyle.FixedSingle };
        }
        private static void AddButton(Control parent, string label, int width, Action handler)
        { var button = new Button { Text = label, Width = width, Height = 33 }; button.Click += delegate { handler(); }; parent.Controls.Add(button); }
        private void OpenFile(bool isBase)
        {
            using (var picker = new OpenFileDialog { Filter = "Leituras e referências (*.json;*.nac;*.cal)|*.json;*.nac;*.cal|Todos os arquivos (*.*)|*.*", Title = isBase ? "Abrir leitura base" : "Abrir referência para comparar" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { var data = OfflineData.Load(picker.FileName); if (isBase) left = data; else right = data; RefreshAll(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Arquivo não carregado"); }
            }
        }
        private void OpenMap()
        {
            using (var picker = new OpenFileDialog { Filter = "Mapa NAC JSON (*.json)|*.json" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { map = OfflineData.LoadMap(picker.FileName); RefreshAll(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Mapa não carregado"); }
            }
        }
        private void RefreshAll()
        {
            labels.Text = "Base: " + (left == null ? "selecione uma leitura salva" : left.Name + " | " + left.Kind) + Environment.NewLine +
                "Referência: " + (right == null ? "selecione outro arquivo" : right.Name + " | " + right.Kind) + Environment.NewLine +
                "Mapa: " + (map == null ? "não carregado" : map.ZoneCount + " zonas / " + map.Fields.Count + " parâmetros NAC; B1 da AIO não tem correspondência neste mapa");
            var ids = new SortedSet<string>(); if (left != null) ids.UnionWith(left.Status.Keys); if (right != null) ids.UnionWith(right.Status.Keys);
            zones.Rows.Clear();
            foreach (string id in ids)
            {
                bool a = left != null && left.Data.ContainsKey(id), b = right != null && right.Data.ContainsKey(id);
                string state = OfflineData.ZoneState(left, right, id);
                int row = zones.Rows.Add(id, map != null && map.ZoneNames.ContainsKey(id) ? map.ZoneNames[id] : "Sem mapa para este ID",
                    a ? (object)(left.Data[id].Length / 2) : "—", b ? (object)(right.Data[id].Length / 2) : "—", state);
                if (state == "Diferente") zones.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 210);
            }
            overview.Text = "Leitura offline. Comparação somente por ID exato; endereços CAL não são zonas. Selecione uma zona para ver os bytes.";
            records.Rows.Clear();
            foreach (var record in OfflineData.CompareRecords(left, right))
            {
                int index = records.Rows.Add(record.Key, record.State, record.Left, record.Right);
                if (record.State == "Diferente") records.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 210);
            }
            notes.Text = OfflineData.Report(left, right, map);
            RefreshBytes(); RefreshFields();
        }
        private void RefreshBytes()
        {
            bytes.Rows.Clear(); if (zones.SelectedRows.Count == 0) return;
            string zone = (string)zones.SelectedRows[0].Cells[0].Value;
            overview.Text = "Zona " + zone + ": base " + (left != null && left.Status.ContainsKey(zone) ? left.Status[zone] : "não disponível") +
                "; referência " + (right != null && right.Status.ContainsKey(zone) ? right.Status[zone] : "não disponível") + ". Offsets abaixo iniciam em zero.";
            string a = left != null && left.Data.ContainsKey(zone) ? left.Data[zone] : "";
            string b = right != null && right.Data.ContainsKey(zone) ? right.Data[zone] : "";
            foreach (var row in OfflineData.Diff(a, b))
            {
                if (changesOnly.Checked && row.Equal) continue;
                int index = bytes.Rows.Add(row.Offset + " / 0x" + row.Offset.ToString("X4"), row.Left, row.Right, row.Xor, row.Bits);
                if (!row.Equal && row.Left != "—" && row.Right != "—") bytes.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 210);
            }
        }
        private void RefreshFields()
        {
            fields.Rows.Clear(); if (map == null) return;
            string search = filter.Text.Trim();
            foreach (var field in map.Fields)
            {
                if (search != "" && (field.Zone + " " + field.ZoneName + " " + field.Name + " " + field.Description).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                fields.Rows.Add(field.Zone, field.Name, field.Description, field.Position + " / " + field.Mask.ToString("X2"), OfflineData.Decode(field, left), OfflineData.Decode(field, right));
            }
        }
        private void Export()
        {
            using (var picker = new SaveFileDialog { Filter = "Relatório de comparação (*.txt)|*.txt", FileName = "comparacao-aio.txt" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(picker.FileName, OfflineData.Report(left, right, map), new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha ao salvar relatório"); }
            }
        }
        public void Demo(bool showMap)
        {
            left = OfflineData.Parse("B1=01020304", ".nac"); left.Name = "base fictícia";
            right = OfflineData.Parse("B1=01028304", ".nac"); right.Name = "referência fictícia";
            RefreshAll(); filter.Text = "camera";
            if (showMap) SelectMapTab(this);
        }
        private static void SelectMapTab(Control parent)
        { foreach (Control child in parent.Controls) { var tabs = child as TabControl; if (tabs != null) tabs.SelectedIndex = 1; else SelectMapTab(child); } }
    }

    internal static class OfflineTests
    {
        private static int count;
        private static void Check(bool value, string label) { if (!value) throw new Exception("Offline: " + label); count++; }
        private static string Record(string type, string address, string data)
        {
            string body = ((address.Length + data.Length) / 2 + 1).ToString("X2") + address + data;
            int sum = 0; for (int i = 0; i < body.Length; i += 2) sum += Convert.ToInt32(body.Substring(i, 2), 16);
            return type + body + ((~sum) & 255).ToString("X2");
        }
        public static int Run()
        {
            var a = OfflineData.Parse("# comment\nB1=0102FF;B2=;2106=80", ".nac");
            var b = OfflineData.Parse("B1=01027F00\n2106=00", ".nac");
            Check(a.Data["B1"] == "0102FF" && !a.Data.ContainsKey("B2"), "leitura .nac e zona sem dados");
            var diff = OfflineData.Diff(a.Data["B1"], b.Data["B1"]);
            Check(diff.Count == 4 && diff[2].Xor == "80" && diff[2].Bits == "10000000", "diferença de bit");
            Check(diff[3].Left == "—" && diff[3].Xor == "—", "dado ausente não vira zero");
            Check(OfflineData.ZoneState(a, b, "B1") == "Diferente" && OfflineData.ZoneState(a, b, "B2") == "Sem dados nos dois arquivos", "diferenças de presença");
            bool duplicate = false; try { OfflineData.Parse("B1=00\nB1=01", ".nac"); } catch (FormatException) { duplicate = true; }
            Check(duplicate, "IDs duplicados rejeitados");
            bool invalid = false; try { OfflineData.Parse("B1=0G", ".nac"); } catch (FormatException) { invalid = true; }
            Check(invalid, "hex inválido rejeitado");
            string mapText = "{\"NAC\":{\"zones\":{\"2106\":{\"name\":\"AVR\",\"params\":[{\"name\":\"DR3\",\"extra_name\":\"REAR_VC\",\"pos\":\"04\",\"mask\":\"80\",\"detail\":{\"en\":\"Camera\"},\"listbox\":[{\"value\":\"80\",\"text\":{\"en\":\"Present\"}},{\"value\":\"00\",\"text\":{\"en\":\"Absent\"}}]}]}}}}";
            var map = OfflineData.ParseMap(mapText);
            Check(map.Fields.Count == 1 && OfflineData.Decode(map.Fields[0], a).StartsWith("Present"), "máscara e posição -4 do mapa");
            Check(OfflineData.Decode(map.Fields[0], b).StartsWith("Absent"), "rótulo alternativo");
            Check(!map.ZoneNames.ContainsKey("B1"), "nenhuma equivalência artificial B1 e 2106");
            string json = MainForm.MakeExport("SIMULAÇÃO", new[] { Protocol.ParseZone("B1", "61B10102") }, new string[0], DateTimeOffset.Now);
            Check(OfflineData.Parse(json, ".json").Data["B1"] == "0102", "exportação AIO reaberta offline");
            bool conflict = false; try { OfflineData.Parse(json.Replace("\"Hex\":\"0102\"", "\"Hex\":\"FFFF\""), ".json"); } catch (FormatException) { conflict = true; }
            Check(conflict, "JSON com conteúdo e resposta inconsistentes rejeitado");
            string cal = Record("S0", "0000", "240000000101810000") + "\n" + Record("S1", "0000", "FFFFD91C0DF56802FF06937622") + "\n" + Record("S2", "00000D", "010203") + "\nS801FE";
            var parsedCal = OfflineData.Parse(cal, ".cal");
            Check(parsedCal.Records.Count == 4 && parsedCal.Records[2].Address == "00000D", "S-record e terminador do projeto");
            Check(parsedCal.Data.Count == 0 && parsedCal.Summary.Contains("D91C"), "CAL sem zonas inventadas e cabeçalho preservado");
            bool checksum = false; try { OfflineData.Parse(cal.Replace("010203", "010204"), ".cal"); } catch (FormatException) { checksum = true; }
            Check(checksum, "checksum S-record inválido rejeitado");
            Check(OfflineData.Report(a, parsedCal, map).Contains("Nenhuma zona com dados tem o mesmo ID"), "CAL não confundido com configuração B1");
            Check(OfflineData.CompareRecords(parsedCal, parsedCal).All(r => r.State == "Idêntico"), "comparação de registros CAL idênticos");
            var duplicateRecords = OfflineData.Parse(Record("S2", "000001", "01") + "\n" + Record("S2", "000001", "02") + "\nS801FE", ".cal");
            Check(OfflineData.CompareRecords(duplicateRecords, duplicateRecords).Count == 3, "registros do mesmo endereço preservados por ocorrência");
            return count;
        }
    }
}
