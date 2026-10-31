// Independent diagnostic client. Protocol references are listed in LEIA-ME.txt.
// No code from the desktop fork is incorporated into this implementation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("AIO Assist Diagnostico")]
[assembly: AssemblyVersion("0.6.1.0")]

namespace AioAssist
{
    public interface ITransport : IDisposable
    {
        void Send(string command);
        Task<string> ReadLineAsync(CancellationToken token);
    }

    public sealed class LineFramer
    {
        private readonly StringBuilder pending = new StringBuilder();
        private readonly Queue<string> complete = new Queue<string>();
        public void Feed(string chunk)
        {
            foreach (char c in chunk)
            {
                if (c == '\n')
                {
                    string line = pending.ToString().Trim(); pending.Clear();
                    if (line.Length > 0) complete.Enqueue(line);
                }
                else if (c != '\r') pending.Append(c);
                if (pending.Length > 65536) throw new IOException("Resposta serial excedeu o limite. Operação interrompida.");
            }
        }
        public string Take() { return complete.Count > 0 ? complete.Dequeue() : null; }
    }

    public sealed class SerialTransport : ITransport
    {
        private readonly SerialPort port;
        private readonly LineFramer framer = new LineFramer();
        public SerialTransport(string name)
        {
            port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One);
            port.Handshake = Handshake.None;
            port.NewLine = "\n";
            port.Encoding = Encoding.ASCII;
            port.DtrEnable = true;
            port.RtsEnable = false;
            port.ReadTimeout = 250;
            port.WriteTimeout = 1000;
            try { port.Open(); }
            catch { port.Dispose(); throw; }
        }
        public void Send(string command)
        {
            Protocol.ValidateCommand(command);
            port.WriteLine(command);
        }
        public async Task<string> ReadLineAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string line = framer.Take();
                if (line != null) return line;
                framer.Feed(port.ReadExisting());
                line = framer.Take();
                if (line != null) return line;
                await Task.Delay(35, token).ConfigureAwait(false);
            }
        }
        public void Dispose() { port.Dispose(); }
    }

    public sealed class ZoneResult
    {
        public string Zone { get; set; }
        public string Description { get; set; }
        public string Response { get; set; }
        public string Hex { get; set; }
        public string Text { get; set; }
        public string Status { get; set; }
    }

    public static class Protocol
    {
        public static readonly string[] Zones = { "FE", "B0", "82", "80", "B1", "B2" };
        // B1/B2 are prioritized. Serial number 82 is optional and is not needed for camera diagnosis.
        public static readonly string[] DiagnosticZones = { "FE", "B0", "B1", "B2", "80" };
        public static string Description(string zone)
        {
            switch (zone)
            {
                case "FE": return "Identificação e calibração";
                case "B0": return "VIN";
                case "82": return "Número de série";
                case "80": return "Zona ZA sem mapa validado";
                case "B1": return "Configuração AIO B1 (mapa PSA-RE experimental)";
                case "B2": return "Configuração sem mapa validado";
                default: throw new ArgumentException("Zona fora do conjunto de diagnóstico.");
            }
        }
        public static void ValidateCommand(string command)
        {
            if (command == "V" || command == ">764:664" || command == ":D91C:83:C0" ||
                command == "KK" || command == "S") return;
            if (Zones.Any(z => command == "21" + z)) return;
            if (Regex.IsMatch(command ?? "", "\\A3BB1[0-9A-F]{360}\\z")) return;
            throw new InvalidOperationException("Comando bloqueado: somente leituras e gravação controlada de B1 (180 bytes).");
        }
        public static string NegativeDescription(string response)
        {
            if (!Regex.IsMatch(response, "^7F[0-9A-F]{4}$")) return "Resposta inesperada";
            string reason;
            switch (response.Substring(4, 2))
            {
                case "12": reason = "Subfunção não suportada ou formato inválido no KWP"; break;
                case "22": reason = "Condições não atendidas"; break;
                case "31": reason = "Solicitação fora da faixa"; break;
                case "33": reason = "Acesso de segurança negado"; break;
                case "35": reason = "Chave recusada"; break;
                case "36": reason = "Limite de tentativas atingido; não repetir"; break;
                case "37": reason = "Tempo de espera obrigatório; não repetir"; break;
                case "78": reason = "Resposta pendente"; break;
                default: reason = "Resposta negativa"; break;
            }
            return reason + " (" + response + ")";
        }
        public static string Ascii(string hex)
        {
            if (hex.Length % 2 != 0 || !Regex.IsMatch(hex, "^[0-9A-F]*$")) throw new FormatException("Hexadecimal inválido.");
            var result = new StringBuilder();
            for (int i = 0; i < hex.Length; i += 2)
            {
                int b = Convert.ToInt32(hex.Substring(i, 2), 16);
                result.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }
            return result.ToString();
        }
        public static ZoneResult ParseZone(string zone, string response)
        {
            Description(zone); // validate zone
            var result = new ZoneResult { Zone = zone, Description = Description(zone), Response = response, Hex = "", Text = "" };
            if (response.StartsWith("7F")) { result.Status = NegativeDescription(response); return result; }
            if (!response.StartsWith("61" + zone) || response.Length <= 4 || response.Length % 2 != 0 ||
                !Regex.IsMatch(response, "^[0-9A-F]+$")) throw new FormatException("Resposta não corresponde à zona " + zone + ".");
            result.Hex = response.Substring(4);
            result.Text = Ascii(result.Hex);
            result.Status = "Lida";
            if (zone == "B0" && !Regex.IsMatch(result.Text, "^[A-HJ-NPR-Z0-9]{17}$"))
                result.Status = "Lida; conteúdo não reconhecido como VIN de 17 caracteres";
            return result;
        }
    }

    public sealed class ResponseTimeoutException : TimeoutException
    {
        public string LastResponse { get; private set; }
        public ResponseTimeoutException(string message, string lastResponse) : base(message)
        { LastResponse = lastResponse; }
    }

    public sealed class Session
    {
        private readonly ITransport wire;
        private readonly Action<string> log;
        private readonly int timeout;
        public bool Ready { get; private set; }
        public string Firmware { get; private set; }
        public bool LastWriteAttempted { get; private set; }
        public bool CanRetryLastWrite { get { return Ready && !LastWriteAttempted; } }
        public Session(ITransport transport, Action<string> logger, int timeoutMs)
        { wire = transport; log = logger; timeout = timeoutMs; }

        private async Task<string> Request(string command, Func<string, bool> expected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Protocol.ValidateCommand(command);
            string lastResponse = "";
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                int requestTimeout = command.StartsWith("21") ? Math.Min(timeout, 5000) : timeout;
                limit.CancelAfter(requestTimeout);
                log("> " + command);
                wire.Send(command);
                while (true)
                {
                    string response;
                    try { response = (await wire.ReadLineAsync(limit.Token).ConfigureAwait(false)).Trim().ToUpperInvariant(); }
                    catch (OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new ResponseTimeoutException("Sem resposta final em " + requestTimeout / 1000.0 + " s para " + command + ". Nenhuma repetição automática.", lastResponse);
                    }
                    log("< " + response);
                    lastResponse = response;
                    if (Regex.IsMatch(response, "^7F[0-9A-F]{4}$"))
                    {
                        string service = response.Substring(2, 2);
                        bool related = command.StartsWith(":") ? (service == "10" || service == "27") :
                            command.Length >= 2 && service == command.Substring(0, 2);
                        if (related && response.EndsWith("78")) continue;
                        if (related) return response;
                    }
                    if (expected(response)) return response;
                }
            }
        }
        public async Task StartAsync(CancellationToken token)
        {
            Ready = false;
            Firmware = await Request("V", s => Regex.IsMatch(s, "^\\d+\\.\\d+$"), token).ConfigureAwait(false);
            if (Firmware != "2.0") throw new InvalidOperationException("Sketch " + Firmware + ": esta versão foi preparada para o sketch 2.0 usado no teste.");
            await ExpectAsync(">764:664", "OK", token).ConfigureAwait(false);
            // The sketch performs seed/key internally and already enables KWP keep-alive.
            // Intermediate 50C0 and 6783 responses are logged, not mistaken for success.
            await ExpectAsync(":D91C:83:C0", "6784", token).ConfigureAwait(false);
            await ExpectAsync("KK", "OK", token).ConfigureAwait(false);
            Ready = true;
        }
        private async Task ExpectAsync(string command, string expected, CancellationToken token)
        {
            string response = await Request(command, s => s == expected, token).ConfigureAwait(false);
            if (response != expected) throw new InvalidOperationException(Protocol.NegativeDescription(response));
        }
        public async Task<ZoneResult> ReadAsync(string zone, CancellationToken token)
        {
            if (!Ready) throw new InvalidOperationException("Inicie uma sessão KWP primeiro.");
            Protocol.Description(zone);
            string response = await Request("21" + zone, s => s.StartsWith("61" + zone), token).ConfigureAwait(false);
            return Protocol.ParseZone(zone, response);
        }
        public async Task WriteB1Async(AioMap map, AioWritePlan plan, string backupPath, CancellationToken token, bool checkCurrent = true)
        {
            LastWriteAttempted = false;
            if (!Ready) throw new InvalidOperationException("Sessão não está pronta.");
            plan.Validate(map);
            if (checkCurrent)
            {
                var fe = await ReadAsync("FE", token).ConfigureAwait(false);
                var vin = await ReadAsync("B0", token).ConfigureAwait(false);
                var b1 = await ReadAsync("B1", token).ConfigureAwait(false);
                if (fe.Hex != plan.Fe || vin.Hex != plan.Vin || b1.Hex != plan.Original)
                    throw new InvalidOperationException("FE, VIN ou B1 mudou desde a leitura. Releia e prepare novamente; nenhuma escrita enviada.");
            }
            else log("Reenvio manual: enviando o B1 preparado sem nova leitura prévia. Backup contém a leitura base anterior.");
            token.ThrowIfCancellationRequested();
            plan.SaveBackup(map, backupPath);
            log("Backup B1 original salvo: " + backupPath);
            bool attempted = false, accepted = false, verified = false;
            string readback = "", failure = "";
            try
            {
                token.ThrowIfCancellationRequested();
                attempted = true;
                LastWriteAttempted = true;
                // Never retry an EEPROM write. After sending, finish readback independently of UI cancellation.
                string response = await Request("3BB1" + plan.Proposed, s => s == "7BB1", CancellationToken.None).ConfigureAwait(false);
                if (response != "7BB1") throw new InvalidOperationException(Protocol.NegativeDescription(response));
                accepted = true;
                var after = await ReadAsync("B1", CancellationToken.None).ConfigureAwait(false);
                readback = after.Hex;
                if (readback != plan.Proposed) throw new InvalidOperationException("Releitura diferente da proposta. Não houve confirmação dos 180 bytes.");
                verified = true;
                log("B1: resposta 7BB1 e releitura de 180 bytes idêntica à proposta. Efeito funcional ainda não verificado.");
            }
            catch (Exception ex) { failure = ex.Message; if (attempted) Ready = false; throw; }
            finally
            {
                try
                {
                    string json = OfflineData.Serializer().Serialize(new { schema = "aio-b1-write-result-v1", time = DateTimeOffset.Now.ToString("o"),
                        attempted = attempted, accepted = accepted, verified = verified, readback = readback, error = failure, backup = backupPath });
                    using (var file = new FileStream(backupPath + ".result.json", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    { byte[] bytes = new UTF8Encoding(false).GetBytes(json); file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                }
                catch (Exception ex) { log("Não foi possível salvar o recibo: " + ex.Message + ". O backup original permanece no caminho informado."); }
            }
        }
        public async Task ReadDiagnosticsAsync(Action<string> progress, Action<ZoneResult> store, CancellationToken token)
        {
            foreach (string zone in Protocol.DiagnosticZones)
            {
                token.ThrowIfCancellationRequested();
                progress(zone);
                ZoneResult result;
                try { result = await ReadAsync(zone, token); }
                catch (ResponseTimeoutException ex)
                {
                    result = new ZoneResult {
                        Zone = zone, Description = Protocol.Description(zone), Hex = "", Text = "",
                        Response = ex.LastResponse,
                        Status = "Sem resposta final no prazo; zona não lida"
                    };
                    log("Zona " + zone + ": " + ex.Message + " Continuando somente as leituras restantes.");
                }
                store(result);
                if (result.Response.StartsWith("7F") && (result.Response.EndsWith("36") || result.Response.EndsWith("37")))
                    throw new InvalidOperationException(result.Status);
            }
        }
        public void Stop()
        {
            Ready = false;
            if (Firmware != "2.0") return;
            try { log("> S"); wire.Send("S"); } catch (Exception ex) { log("Não foi possível parar keep-alive: " + ex.Message); }
        }
    }

    public sealed class MainForm : Form
    {
        private readonly ComboBox ports = new ComboBox();
        private readonly Button refresh = new Button();
        private readonly Button connect = new Button();
        private readonly Button disconnect = new Button();
        private readonly Button readAll = new Button();
        private readonly Button readVin = new Button();
        private readonly Button export = new Button();
        private readonly Button cancel = new Button();
        private readonly Button offline = new Button();
        private readonly Button parameters = new Button();
        private readonly Button configurationOffline = new Button();
        private readonly Label state = new Label();
        private readonly Label vin = new Label();
        private readonly Label calibration = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox transcript = new TextBox();
        private readonly Dictionary<string, ZoneResult> results = new Dictionary<string, ZoneResult>();
        private readonly List<string> logs = new List<string>();
        private ITransport wire;
        private Session session;
        private CancellationTokenSource operation;
        private bool busy;
        private string portName = "";
        public MainForm(bool discoverPorts)
        {
            Text = "AIO Assist 0.6.1 | Diagnóstico e parâmetros KWP";
            Size = new Size(1100, 800); MinimumSize = new Size(860, 650);
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White; StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(18) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "AIO RCC A1  •  Diagnóstico e parâmetros B1", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17, FontStyle.Bold) }, 0, 0);
            var connectionBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            ports.DropDownStyle = ComboBoxStyle.DropDownList; ports.Width = 105;
            SetupButton(refresh, "Atualizar portas", 140);
            SetupButton(connect, "Conectar e iniciar sessão", 225);
            SetupButton(disconnect, "Desconectar", 130);
            SetupButton(cancel, "Interromper leitura", 170);
            connectionBar.Controls.AddRange(new Control[] { ports, refresh, connect, disconnect, cancel });
            root.Controls.Add(connectionBar, 0, 1);
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            state.Text = "Desconectado"; state.AutoSize = true;
            vin.Text = "VIN: ainda não lido"; vin.AutoSize = true;
            calibration.Text = "Calibração: ainda não lida"; calibration.AutoSize = true;
            details.Controls.Add(state, 0, 0); details.SetColumnSpan(state, 2);
            details.Controls.Add(vin, 0, 1); details.Controls.Add(calibration, 1, 1);
            root.Controls.Add(details, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            SetupButton(readAll, "Ler zonas", 125);
            SetupButton(readVin, "Reler VIN", 110);
            SetupButton(export, "Exportar leituras", 155);
            SetupButton(offline, "Comparação offline", 175);
            SetupButton(parameters, "Parâmetros AIO B1", 185);
            SetupButton(configurationOffline, "Configuração offline", 180);
            actions.Controls.AddRange(new Control[] { readAll, readVin, export, offline, parameters, configurationOffline });
            root.Controls.Add(actions, 0, 3);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.FixedSingle;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
            grid.Columns.Add("zone", "Zona"); grid.Columns.Add("name", "Descrição"); grid.Columns.Add("hex", "Dados hexadecimais"); grid.Columns.Add("status", "Resultado");
            grid.Columns[0].FillWeight = 13; grid.Columns[1].FillWeight = 50; grid.Columns[2].FillWeight = 90; grid.Columns[3].FillWeight = 55;
            root.Controls.Add(grid, 0, 4);
            root.Controls.Add(new Label { Text = "Comunicação serial — respostas completas", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 5);
            transcript.Multiline = true; transcript.ReadOnly = true; transcript.ScrollBars = ScrollBars.Both;
            transcript.WordWrap = false; transcript.Dock = DockStyle.Fill; transcript.Font = new Font("Consolas", 9);
            root.Controls.Add(transcript, 0, 6);
            root.Controls.Add(new Label { Text = "Experimental: edição de B1 com backup e releitura. Sem upload de calibração/firmware. Exportação parcial.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 75, 75), Padding = new Padding(0, 9, 0, 0) }, 0, 7);
            refresh.Click += delegate { RefreshPorts(); };
            connect.Click += async delegate { await RunAsync(ConnectAsync); };
            readVin.Click += async delegate { await RunAsync(async t => Store(await session.ReadAsync("B0", t))); };
            readAll.Click += async delegate { await RunAsync(ReadAllAsync); };
            disconnect.Click += delegate { Disconnect(); };
            cancel.Click += delegate { if (operation != null) operation.Cancel(); };
            export.Click += delegate { Export(); };
            configurationOffline.Click += delegate {
                try { using (var viewer = new AioOfflineView(true)) viewer.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Consulta offline"); }
            };
            parameters.Click += delegate {
                try { using (var editor = new AioParameterForm(session, Log)) editor.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Editor AIO"); }
                // The parameter editor owns its fresh reads. Do not export stale main-window values as current.
                results.Remove("B1");
                foreach (DataGridViewRow row in grid.Rows)
                    if (Convert.ToString(row.Cells[0].Value) == "B1") { row.Cells[2].Value = ""; row.Cells[3].Value = "Reler após fechar o editor"; }
                UpdateControls();
            };
            offline.Click += delegate {
                Snapshot current = null;
                if (results.Count > 0)
                {
                    current = OfflineData.Parse(MakeExport(portName, results.Values.ToArray(), logs.ToArray(), DateTimeOffset.Now), ".json");
                    current.Name = "Leitura atual em memória";
                }
                using (var viewer = new OfflineForm(current)) viewer.ShowDialog(this);
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (busy) { if (operation != null) operation.Cancel(); args.Cancel = true; Log("Operação interrompida. Aguarde a conclusão e feche novamente."); }
                else Disconnect();
            };
            if (discoverPorts) RefreshPorts();
            UpdateControls();
        }
        private static void SetupButton(Button button, string text, int width)
        { button.Text = text; button.Size = new Size(width, 33); button.Margin = new Padding(5, 0, 3, 0); }
        private void RefreshPorts()
        {
            string old = ports.Text;
            try
            {
                ports.Items.Clear(); ports.Items.AddRange(SerialPort.GetPortNames().OrderBy(s => s).ToArray());
                if (ports.Items.Contains(old)) ports.SelectedItem = old;
                else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
            }
            catch (Exception ex) { Log("Não foi possível listar portas: " + ex.Message); }
            UpdateControls();
        }
        private void UpdateControls()
        {
            bool ready = session != null && session.Ready;
            ports.Enabled = !busy && wire == null; refresh.Enabled = ports.Enabled;
            connect.Enabled = !busy && wire == null && ports.SelectedIndex >= 0;
            disconnect.Enabled = !busy && wire != null; cancel.Enabled = busy;
            readAll.Enabled = !busy && ready; readVin.Enabled = !busy && ready;
            export.Enabled = !busy && logs.Count > 0;
            offline.Enabled = !busy;
            parameters.Enabled = !busy;
            configurationOffline.Enabled = !busy;
        }
        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true; operation = new CancellationTokenSource(); UpdateControls();
            try { await action(operation.Token); }
            catch (OperationCanceledException) { Log("Leitura interrompida."); Disconnect(); state.Text = "Interrompido; reconecte para iniciar nova sessão"; }
            catch (Exception ex)
            {
                Log("ERRO: " + ex.Message); Disconnect(); state.Text = "Falha; verifique o log antes de reconectar";
                MessageBox.Show(this, ex.Message, "AIO Assist", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { operation.Dispose(); operation = null; busy = false; UpdateControls(); }
        }
        private async Task ConnectAsync(CancellationToken token)
        {
            if (ports.SelectedIndex < 0) throw new InvalidOperationException("Selecione a porta COM do Arduino.");
            results.Clear(); grid.Rows.Clear(); vin.Text = "VIN: ainda não lido"; calibration.Text = "Calibração: ainda não lida";
            portName = ports.Text; state.Text = "Abrindo " + portName + " — feche outros aplicativos seriais";
            Log("Nova sessão em " + portName + ". Ignicão ligada, motor desligado; interface de diagnóstico KWP.");
            wire = await Task.Run<ITransport>(() => new SerialTransport(portName));
            state.Text = "Aguardando inicialização do Uno...";
            await Task.Delay(2300, token);
            session = new Session(wire, Log, 10000);
            state.Text = "Identificando sketch e desbloqueando AIO...";
            await session.StartAsync(token);
            state.Text = "Desbloqueio aceito — manutenção KWP solicitada; lendo identificação";
            Store(await session.ReadAsync("FE", token));
            Store(await session.ReadAsync("B0", token));
            state.Text = "Sessão iniciada — releia o VIN após 15 s para testar permanência";
            Log("Sessão pronta. Keep-alive é enviado pelo sketch; KK foi aceito. Não é prova contínua de sessão ativa.");
        }
        private async Task ReadAllAsync(CancellationToken token)
        {
            Log("Leitura FE, B0, B1, B2 e 80. Consulta opcional 2182 omitida por não concluir no teste anterior.");
            await session.ReadDiagnosticsAsync(z => state.Text = "Lendo zona " + z + "...", Store, token);
            state.Text = "Leitura concluída — confira os resultados por zona";
            Log("B1 e B2 não têm mapa de câmera validado nesta versão. Leitura não habilita edição.");
        }
        private void Store(ZoneResult result)
        {
            results[result.Zone] = result;
            DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (string)r.Cells[0].Value == result.Zone);
            if (row == null) { int index = grid.Rows.Add(); row = grid.Rows[index]; }
            row.SetValues(result.Zone, result.Description, result.Hex, result.Status);
            if (result.Zone == "B0") vin.Text = result.Status == "Lida" ? "VIN: " + result.Text : "VIN: leitura não validada";
            if (result.Zone == "FE") calibration.Text = result.Status == "Lida" && result.Hex.Length >= 6 ?
                "Referência CAL inferida: 96" + result.Hex.Substring(result.Hex.Length - 6) + "80" : "Calibração: leitura indisponível";
        }
        private void Log(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            string line = DateTimeOffset.Now.ToString("HH:mm:ss.fff zzz") + " " + message;
            logs.Add(line); transcript.AppendText(line + Environment.NewLine);
        }
        private void Disconnect()
        {
            if (session != null) session.Stop();
            session = null;
            if (wire != null)
            {
                try { wire.Dispose(); } catch (Exception ex) { Log("Erro ao fechar porta: " + ex.Message); }
                wire = null;
            }
            state.Text = "Desconectado — manutenção automática encerrada";
            UpdateControls();
        }
        private void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "Leitura AIO JSON (*.json)|*.json", FileName = "aio-leitura-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string json = MakeExport(portName, results.Values.ToArray(), logs.ToArray(), DateTimeOffset.Now);
                    File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
                    Log("Leituras e log exportados localmente. Não é backup completo nem arquivo para Restore Parameters.");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha na exportação"); }
            }
        }
        public static string MakeExport(string port, ZoneResult[] zones, string[] transcript, DateTimeOffset timestamp)
        {
            return new JavaScriptSerializer().Serialize(new {
                schema = "aio-assist-diagnostic-v1", applicationVersion = "0.6.1", capturedAt = timestamp.ToString("o"),
                port = port, protocol = "KWP C0", completeBackup = false, cameraMappingValidated = false,
                zones = zones, log = transcript
            });
        }
        public void Demo()
        {
            ports.Items.Add("SIMULAÇÃO"); ports.SelectedIndex = 0;
            state.Text = "SIMULAÇÃO — nenhuma porta aberta; exemplos sem dados do veículo";
            Store(Protocol.ParseZone("FE", "61FEFFFF00000DF50101016808FF02010101FD02000001931037"));
            Store(Protocol.ParseZone("B0", "61B0" + string.Concat("VF700000000000001".Select(c => ((int)c).ToString("X2")))));
            Store(Protocol.ParseZone("B1", "61B10102030405060708"));
            Store(Protocol.ParseZone("B2", "7F2131"));
            Log("SIMULAÇÃO de interface. Nenhum dispositivo acessado.");
            Log("> :D91C:83:C0"); Log("< 50C0"); Log("< 6783XXXXXXXX"); Log("< 6784"); Log("> KK"); Log("< OK");
            UpdateControls(); connect.Enabled = false;
        }
    }

    internal sealed class FakeTransport : ITransport
    {
        public readonly List<string> Sent = new List<string>();
        public readonly Queue<KeyValuePair<string, string[]>> Script = new Queue<KeyValuePair<string, string[]>>();
        private readonly Queue<string> responses = new Queue<string>();
        public void Add(string command, params string[] replies) { Script.Enqueue(new KeyValuePair<string, string[]>(command, replies)); }
        public void Send(string command)
        {
            Protocol.ValidateCommand(command); Sent.Add(command);
            if (Script.Count == 0) throw new Exception("Comando inesperado: " + command);
            var step = Script.Dequeue();
            if (step.Key != command) throw new Exception("Esperado " + step.Key + "; recebido " + command);
            foreach (string reply in step.Value) responses.Enqueue(reply);
        }
        public async Task<string> ReadLineAsync(CancellationToken token)
        {
            while (responses.Count == 0) await Task.Delay(5, token).ConfigureAwait(false);
            return responses.Dequeue();
        }
        public void Dispose() { }
    }

    internal static class Tests
    {
        private static int count;
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception("FALHOU: " + label); count++; }
        private static FakeTransport Startup(params string[] unlockReplies)
        {
            var fake = new FakeTransport(); fake.Add("V", "2.0"); fake.Add(">764:664", "OK");
            fake.Add(":D91C:83:C0", unlockReplies); return fake;
        }
        public static async Task<string> Run()
        {
            var framing = new LineFramer();
            framing.Feed("61B03842"); Check(framing.Take() == null, "fragmento não tratado como linha completa");
            framing.Feed("434E\r\n\r\nOK\n");
            Check(framing.Take() == "61B03842434E", "fragmentos seriais reunidos");
            Check(framing.Take() == "OK" && framing.Take() == null, "linhas múltiplas e CRLF");
            bool overflow = false;
            try { new LineFramer().Feed(new string('A', 65537)); } catch (IOException) { overflow = true; }
            Check(overflow, "limite da linha serial");
            var token = CancellationToken.None;
            var fake = Startup("50C0", "6783869FAEBA", "6784"); fake.Add("KK", "OK");
            fake.Add("21FE", "61FEFFFF00000DF50101016808FF02010101FD02000001931037");
            fake.Add("21B0", "7E", "61B05646373030303030303030303030303031");
            fake.Add("21B1", "7F2178", "61B10102"); fake.Add("21B2", "7F2122"); fake.Add("S", "OK");
            var session = new Session(fake, s => { }, 150);
            await session.StartAsync(token); Check(session.Ready, "desbloqueio e KK");
            Check((await session.ReadAsync("FE", token)).Hex.EndsWith("931037"), "identificação original");
            ZoneResult vin = await session.ReadAsync("B0", token);
            Check(vin.Text == "VF700000000000001", "VIN observado decodificado");
            Check((await session.ReadAsync("B1", token)).Hex == "0102", "resposta pendente seguida de sucesso");
            Check((await session.ReadAsync("B2", token)).Status.Contains("Condições"), "NRC22 não confundido com sucesso");
            string json = MainForm.MakeExport("SIMULAÇÃO", new[] { vin }, new[] { "TESTE" }, DateTimeOffset.Now);
            var exported = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            Check((bool)exported["completeBackup"] == false, "exportação declarada parcial");
            session.Stop(); Check(!session.Ready && fake.Script.Count == 0, "parada do keep-alive e sequência completa");
            foreach (string cmd in new[] { "3BB000", "2E210600", "3101FF00", "3481110000", "21FF", "KK\n3BB0", "278400000000" })
            {
                bool blocked = false; try { Protocol.ValidateCommand(cmd); } catch (InvalidOperationException) { blocked = true; }
                Check(blocked, "bloqueio de " + cmd);
            }
            foreach (string response in new[] { "61B001", "61B0564637303030303030303030303030303", "61B0GG" })
            {
                bool rejected = false;
                try { rejected = Protocol.ParseZone("B0", response).Status != "Lida"; } catch (FormatException) { rejected = true; }
                Check(rejected, "VIN ou dados inválidos");
            }
            var denied = Startup("50C0", "678300000000", "7F2735");
            var deniedSession = new Session(denied, s => { }, 100);
            bool failed = false;
            try { await deniedSession.StartAsync(token); } catch (InvalidOperationException) { failed = true; }
            Check(failed && !deniedSession.Ready && denied.Sent.Count == 3, "chave recusada sem repetir");
            var shortSession = new Session(Startup("50C0", "6783ABCDEF00"), s => { }, 80);
            bool timed = false;
            try { await shortSession.StartAsync(token); } catch (TimeoutException) { timed = true; }
            Check(timed && !shortSession.Ready, "seed ou sessão aberta não bastam para liberar botões");
            var old = new FakeTransport(); old.Add("V", "1.0");
            bool wrongVersion = false;
            try { await new Session(old, s => { }, 100).StartAsync(token); } catch (InvalidOperationException) { wrongVersion = true; }
            Check(wrongVersion && old.Sent.Count == 1, "sketch não validado interrompe antes do desbloqueio");
            var cancelled = new FakeTransport(); cancelled.Add("V");
            using (var cts = new CancellationTokenSource(20))
            {
                bool wasCancelled = false;
                try { await new Session(cancelled, s => { }, 1000).StartAsync(cts.Token); } catch (OperationCanceledException) { wasCancelled = true; }
                Check(wasCancelled, "cancelamento independente do timeout");
            }
            var unrelated = Startup("50C0", "6784"); unrelated.Add("KK", "OK"); unrelated.Add("21B0", "61B1FFFF");
            var unrelatedSession = new Session(unrelated, s => { }, 50); await unrelatedSession.StartAsync(token);
            bool mismatch = false;
            try { await unrelatedSession.ReadAsync("B0", token); } catch (TimeoutException) { mismatch = true; }
            Check(mismatch, "resposta de outra zona não é aceite");
            var batchWire = Startup("50C0", "6783583AFABA", "6784"); batchWire.Add("KK", "OK");
            batchWire.Add("21FE", "61FEFFFF00000DF50101016808FF02010101FD02000001931037");
            batchWire.Add("21B0", "61B05646373030303030303030303030303031");
            batchWire.Add("21B1", "7F2178");
            // Late response to B1 must not be misattributed to B2.
            batchWire.Add("21B2", "61B1FFFF", "61B2010203");
            batchWire.Add("2180", "7F2131");
            var batchSession = new Session(batchWire, s => { }, 60);
            await batchSession.StartAsync(token);
            var batchResults = new List<ZoneResult>();
            await batchSession.ReadDiagnosticsAsync(z => { }, batchResults.Add, token);
            Check(batchResults.Count == 5, "timeout em zona não aborta lote de leituras");
            Check(batchResults[2].Zone == "B1" && batchResults[2].Hex == "" && batchResults[2].Response == "7F2178", "pendente preservado sem inventar dados");
            Check(batchResults[3].Zone == "B2" && batchResults[3].Hex == "010203", "resposta tardia de outra zona ignorada");
            Check(batchResults[4].Zone == "80" && batchResults[4].Response == "7F2131", "zona seguinte ainda consultada");
            Check(!batchWire.Sent.Contains("2182") && batchWire.Script.Count == 0, "número de série omitido do lote padrão");
            Check(batchSession.Ready, "timeout opcional não encerra manutenção da sessão");
            var lockedWire = Startup("6784"); lockedWire.Add("KK", "OK");
            lockedWire.Add("21FE", "7F2136");
            var lockedSession = new Session(lockedWire, s => { }, 60); await lockedSession.StartAsync(token);
            bool securityStop = false;
            try { await lockedSession.ReadDiagnosticsAsync(z => { }, r => { }, token); } catch (InvalidOperationException) { securityStop = true; }
            Check(securityStop && lockedWire.Script.Count == 0 && !lockedWire.Sent.Contains("21B0"), "limite de segurança ainda interrompe lote");
            count += OfflineTests.Run();
            count += await AioParameterTests.Run();
            count += AioOfflineViewTests.Run();
            count += AioCrossTests.Run();
            return "PASS: " + count + " verificações. Transporte simulado e arquivos locais; nenhuma porta serial aberta.\r\n";
        }
    }

    internal static class Program
    {
        private static void PrepareSnapshot(Control control)
        {
            IntPtr handle = control.Handle;
            control.PerformLayout();
            foreach (Control child in control.Controls) PrepareSnapshot(child);
            control.PerformLayout();
        }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 5 && args[0] == "--compare")
            {
                try { File.WriteAllText(args[4], OfflineData.Report(OfflineData.Load(args[1]), OfflineData.Load(args[2]), OfflineData.LoadMap(args[3])), new UTF8Encoding(false)); return 0; }
                catch (Exception ex) { File.WriteAllText(args[4], "FAIL: " + ex, Encoding.UTF8); return 1; }
            }
            if (args.Length == 2 && args[0] == "--self-test")
            {
                try { File.WriteAllText(args[1], Tests.Run().GetAwaiter().GetResult(), Encoding.UTF8); return 0; }
                catch (Exception ex) { File.WriteAllText(args[1], "FAIL: " + ex, Encoding.UTF8); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 3 && args[0] == "--snapshot-cross")
            {
                using (var form = new AioCrossView(OfflineData.Load(args[1])))
                {
                    PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[2]); }
                }
                return 0;
            }
            if (args.Length == 0 && Path.GetFileNameWithoutExtension(Application.ExecutablePath).StartsWith("AIO-Consulta-Offline", StringComparison.OrdinalIgnoreCase))
            { Application.Run(new AioOfflineView(true)); return 0; }
            if (args.Length == 1 && args[0] == "--aio-offline") { Application.Run(new AioOfflineView(true)); return 0; }
            if (args.Length == 3 && args[0] == "--snapshot-aio-offline")
            {
                using (var form = new AioOfflineView(false))
                {
                    form.LoadFile(args[1], false); PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[2]); }
                }
                return 0;
            }
            if (args.Length == 4 && args[0] == "--snapshot-aio-offline-compare")
            {
                using (var form = new AioOfflineView(false))
                {
                    form.LoadFile(args[1], false); form.LoadFile(args[2], true); PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[3]); }
                }
                return 0;
            }
            if (args.Length == 3 && args[0] == "--snapshot-parameters")
            {
                using (var form = new AioParameterForm(null, s => { }))
                {
                    form.LoadDemo(OfflineData.Load(args[1])); PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[2]); }
                }
                return 0;
            }
            if (args.Length == 2 && (args[0] == "--snapshot-offline" || args[0] == "--snapshot-map"))
            {
                using (var form = new OfflineForm(null))
                {
                    form.Demo(args[0] == "--snapshot-map"); PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1]); }
                }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--offline") { Application.Run(new OfflineForm(null)); return 0; }
            if (args.Length == 2 && args[0] == "--snapshot")
            {
                using (var form = new MainForm(false))
                {
                    form.Demo(); PrepareSnapshot(form);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1]); }
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--demo")
            { var demo = new MainForm(false); demo.Demo(); Application.Run(demo); return 0; }
            Application.Run(new MainForm(true)); return 0;
        }
    }
}
