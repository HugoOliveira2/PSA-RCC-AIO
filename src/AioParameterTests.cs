using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AioAssist
{
    internal static class AioParameterTests
    {
        private const string Fe = "FFFF00000DF50101016808FF02010101FD02000001931037";
        private const string Vin = "5646373030303030303030303030303031";
        private const string B1 = "0000C02A24240300402C78044000050100000000000000060000000000000000000000000000000000000000001C00000000000000228012000A00401665C80700B0C0AA003C108056EA000002000000009800C0280005121F2C3946535F644000008038000000F4003A000000000000A8B428B41EB4281C0E190AE402D207080106070802D9026606F9021C0804020807D0802802570001F300777269000064640000008023802000C030200000100080000040";
        private static int count;
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FALHOU B1: " + name); count++; }
        private static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
        private static FakeTransport Start()
        {
            var wire = new FakeTransport(); wire.Add("V", "2.0"); wire.Add(">764:664", "OK"); wire.Add(":D91C:83:C0", "50C0", "6783AABBCCDD", "6784"); wire.Add("KK", "OK"); return wire;
        }
        private static AioWritePlan Plan(AioMap map)
        {
            var changes = new Dictionary<string, ulong> { { "AMBIANCE_DEFAULT_000", 3 } };
            return new AioWritePlan { Fe = Fe, Vin = Vin, Original = B1, Changes = changes, Proposed = AioMap.Hex(map.Apply(AioMap.Bytes(B1), changes)) };
        }
        private static void Reads(FakeTransport wire, string fe, string vin, string b1)
        { wire.Add("21FE", "61FE" + fe); wire.Add("21B0", "61B0" + vin); wire.Add("21B1", "61B1" + b1); }
        public static async Task<int> Run()
        {
            count = 0; var map = AioMap.Load(); var data = AioMap.Bytes(B1);
            Check(map.Fields.Count == 292 && data.Length == 180, "292 campos e B1 real com 180 bytes");
            Check(map.Identity(Fe, Vin, B1) == "931037 / E3_V3", "variante do Cactus identificada");
            Check(map.Fields.Single(f => f.Name == "AMBIANCE_DEFAULT_000").Read(data) == 2, "tema da leitura real");
            Check(map.Fields.Single(f => f.Name == "MANUFACTURER_S_BRAND").Read(data) == 1, "marca Citroen da leitura real");
            Check(map.Fields.Single(f => f.Name == "TYPE_AIR_CONDITIONING").Read(data) == 1, "climatizacao simples da leitura real");
            Check(map.Fields.Single(f => f.Name == "TYPE_AIR_CONDITIONING_MIXING").Read(data) == 0, "monozona da leitura real");
            var plan = Plan(map); plan.Validate(map);
            Check(plan.Proposed.Substring(24, 2) == "60" && map.Diff(B1, plan.Proposed).Contains("offset 12"), "tema altera byte 13 de 40 para 60");
            Check(AioMap.Bytes(plan.Proposed).Where((b, i) => b != data[i]).Count() == 1, "nenhum outro byte alterado pelo tema");
            Check(AioMap.Hex(map.Apply(data, new Dictionary<string, ulong>())) == B1, "proposta vazia preserva todos os bytes");
            // Check every editable field with boundary/enum values on nonzero random data.
            var random = new Random(42); var sample = new byte[180]; random.NextBytes(sample);
            foreach (var field in map.Fields.Where(f => f.Editable))
            {
                ulong value = field.Choices.Count == 0 ? field.Max : field.Choices.Keys.Max();
                byte[] output = (byte[])sample.Clone(); field.Put(output, value);
                Check(field.Read(output) == value, "roundtrip " + field.Name);
                bool preserved = true;
                for (int i = 0; i < 1440; i++) if (i < field.Start || i >= field.Start + field.Width)
                    preserved &= ((sample[i / 8] ^ output[i / 8]) & (1 << (7 - i % 8))) == 0;
                Check(preserved, "bits vizinhos preservados " + field.Name);
            }
            Reject(() => AioMap.Bytes("00"), "bloco curto rejeitado");
            Reject(() => AioMap.Bytes(new string('G', 360)), "hex invalido rejeitado");
            Reject(() => map.Identity(Fe.Substring(0, 42) + "000000", Vin, B1), "variante desconhecida rejeitada");
            Reject(() => map.Identity(Fe, "0000", B1), "VIN incompleto rejeitado");
            Reject(() => map.Apply(data, new Dictionary<string, ulong> { { "AMBIANCE_DEFAULT_000", 8 } }), "tema fora de 0 a 7 rejeitado");
            plan.Proposed = new string('0', 360); Reject(() => plan.Validate(map), "proposta adulterada rejeitada"); plan = Plan(map);
            foreach (string cmd in new[] { "3BB1" + B1.Substring(2), "3BB1" + B1 + "00", "3BB0" + B1, "3BB1" + B1 + "\n", "3BB1" + B1 + "\n21B0" })
                Reject(() => Protocol.ValidateCommand(cmd), "comando adulterado rejeitado");
            Protocol.ValidateCommand("3BB1" + plan.Proposed); Check(true, "somente B1 completo permitido");
            string folder = Path.Combine(Path.GetTempPath(), "aio-b1-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                string backup = Path.Combine(folder, "original.json");
                var wire = Start(); Reads(wire, Fe, Vin, B1); wire.Add("3BB1" + plan.Proposed, "7F3B78", "7BB1"); wire.Add("21B1", "61B1" + plan.Proposed);
                var session = new Session(wire, s => { }, 90); await session.StartAsync(CancellationToken.None);
                await session.WriteB1Async(map, plan, backup, CancellationToken.None);
                Check(wire.Script.Count == 0 && wire.Sent.Count(c => c.StartsWith("3B")) == 1, "uma escrita, pendente aceito, releitura completa");
                Check(OfflineData.Load(backup).Data["B1"] == B1, "backup contem B1 original, nao proposta");
                Check(File.ReadAllText(backup + ".result.json").Contains("\"verified\":true"), "recibo de verificacao salvo");
                Reject(() => plan.SaveBackup(map, backup), "backup existente nao sobrescrito");
                var retryWire = Start(); retryWire.Add("21FE", "61FE" + Fe); retryWire.Add("21B0", "61B0" + Vin); retryWire.Add("21B1");
                var retrySession = new Session(retryWire, s => { }, 60); await retrySession.StartAsync(CancellationToken.None);
                bool firstFailed = false;
                try { await retrySession.WriteB1Async(map, plan, Path.Combine(folder, "first.json"), CancellationToken.None); }
                catch (ResponseTimeoutException) { firstFailed = true; }
                Check(firstFailed && retrySession.CanRetryLastWrite && !retryWire.Sent.Any(c => c.StartsWith("3B")), "falha na conferencia permite reenvio manual sem escrita anterior");
                int beforeRetry = retryWire.Sent.Count;
                retryWire.Add("3BB1" + plan.Proposed, "7F3B78", "7BB1"); retryWire.Add("21B1", "61B1" + plan.Proposed);
                string retryPath = Path.Combine(folder, "manual-retry.json");
                await retrySession.WriteB1Async(map, plan, retryPath, CancellationToken.None, false);
                Check(retryWire.Sent.Skip(beforeRetry).SequenceEqual(new[] { "3BB1" + plan.Proposed, "21B1" }), "reenvio envia gravacao diretamente e depois verifica sem leituras previas");
                Check(retryWire.Sent.Count(c => c.StartsWith("3B")) == 1, "uma escrita por clique manual");
                Check(OfflineData.Load(retryPath).Data["B1"] == B1 && File.ReadAllText(retryPath + ".result.json").Contains("\"verified\":true"), "reenvio salva base e confere resultado");
                foreach (string scenario in new[] { "stale", "identity", "backup", "denied", "timeout", "readback" })
                {
                    var fake = Start(); Reads(fake, scenario == "identity" ? Fe.Substring(0, 42) + "937622" : Fe, Vin, scenario == "stale" ? plan.Proposed : B1);
                    if (scenario == "denied") fake.Add("3BB1" + plan.Proposed, "7F3B33");
                    if (scenario == "timeout") fake.Add("3BB1" + plan.Proposed);
                    if (scenario == "readback") { fake.Add("3BB1" + plan.Proposed, "7BB1"); fake.Add("21B1", "61B1" + B1); }
                    var testSession = new Session(fake, s => { }, 60); await testSession.StartAsync(CancellationToken.None);
                    bool failed = false;
                    string path = scenario == "backup" ? Path.Combine(folder, "missing", "a.json") : Path.Combine(folder, scenario + ".json");
                    try { await testSession.WriteB1Async(map, plan, path, CancellationToken.None); } catch { failed = true; }
                    Check(failed, "falha detectada: " + scenario);
                    bool sent = scenario == "denied" || scenario == "timeout" || scenario == "readback";
                    Check(fake.Sent.Count(c => c.StartsWith("3B")) == (sent ? 1 : 0), "sem escrita prematura/repeticao: " + scenario);
                    if (sent) Check(!testSession.Ready && File.ReadAllText(path + ".result.json").Contains("\"verified\":false"), "resultado incerto invalida sessao: " + scenario);
                }
                var cancelWire = Start(); var cancelSession = new Session(cancelWire, s => { }, 60); await cancelSession.StartAsync(CancellationToken.None);
                bool cancelled = false; using (var cts = new CancellationTokenSource())
                { cts.Cancel(); try { await cancelSession.WriteB1Async(map, plan, Path.Combine(folder, "cancel.json"), cts.Token); } catch (OperationCanceledException) { cancelled = true; } }
                // Fake transport may record a cancelled read request; it must never record a write.
                Check(cancelled && !cancelWire.Sent.Any(c => c.StartsWith("3B")), "cancelamento nunca grava");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
            return count;
        }
    }
}
