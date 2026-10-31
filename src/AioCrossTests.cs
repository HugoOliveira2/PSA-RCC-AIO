using System;
using System.IO;
using System.Linq;

namespace AioAssist
{
    internal static class AioCrossTests
    {
        private static int count;
        private static void Check(bool value, string message) { if (!value) throw new Exception("FALHOU cruzamento: " + message); count++; }
        public static int Run()
        {
            count = 0; var data = AioCrossData.Load();
            var snapshot = OfflineData.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "examples", "leitura-anonimizada.json"));
            string original = snapshot.Data["B1"]; byte[] bytes = AioMap.Bytes(original);
            Check(data.Entries.Count == 292, "292 campos AIO, sem reservados extras");
            Check(data.Entries.Count(e => e.Kind == "exact") == 258, "258 nomes exatos");
            Check(data.Entries.Count(e => e.Kind == "position") == 29, "29 candidatos posicionais");
            Check(data.Entries.Count(e => e.Kind == "none") == 5, "5 campos sem correspondente");
            Check(data.Entries.Count(e => e.MissingDescription) == 188, "188 campos sem descricao AIO");
            Check(data.Entries.Count(e => e.MissingDescription && e.Candidates.Any(c => c.Legacy.Length > 0)) == 166, "166 descricoes encontradas no nac.json");
            var theme = data.Entries.Single(e => e.Field.Name == "AMBIANCE_DEFAULT_000");
            Check(theme.Kind == "exact" && theme.Candidates[0].Zone == "2127" && theme.Candidates[0].Bits == "4.7-4.5" && theme.Candidates[0].Delta == 72, "tema conserva bits com deslocamento de 9 bytes");
            Check(data.AioValue(theme, bytes).StartsWith("2 — Código de tema 2"), "valor AIO original preservado");
            Check(!data.Entries.Any(e => e.Field.Name == "AMBIANCE_CURRENT_000"), "campo reservado nao promovido a parametro");
            var climate = data.Entries.Single(e => e.Field.Name == "TYPE_AIR_CONDITIONING");
            Check(climate.Kind == "position" && climate.Candidates[0].Zone == "210D", "alias climatico explicitamente posicional");
            Check(data.Hypothesis(climate, bytes).StartsWith("Hipótese NAC:"), "rotulo transferido marcado como hipotese");
            Check(data.AioValue(climate, bytes).Contains("Ar-condicionado simples"), "interpretacao AIO nao substituida pela NAC");
            var dsp = data.Entries.Single(e => e.Field.Name == "TYPE_EXP_MEDIA_CAN_000").Candidates[0];
            Check(dsp.Legacy.Any(d => OfflineData.Object(d["choices"]).ContainsKey("2")), "valor 0x10 normalizado para codigo 2 sob mascara 0x18");
            var width = data.Entries.Single(e => e.Field.Name == "CPAS_PUSH_POS_000");
            Check(!width.Candidates[0].SameWidth && data.Hypothesis(width, bytes).Contains("largura diferente"), "largura divergente bloqueia interpretacao do valor");
            var conflict = data.Entries.Single(e => e.Field.Name == "NAME_ITEM4_FAMILY_1_001");
            Check(conflict.LinkLabel == "Descrição NAC ambígua" && data.Hypothesis(conflict, bytes).Contains("divergentes"), "erro de indice no NAC nao encoberto");
            var missing = data.Entries.Single(e => e.Field.Name == "PRESENCE_BLUETOOTH_FUNCTION");
            Check(missing.Kind == "none" && missing.Candidates.Count == 0, "nao inventa vinculo Bluetooth");
            Check(AioCrossData.TranslateDescription("Vehicule menu 2 Item 7") == "Menu do veículo 2, item 7", "descricao de menu traduzida");
            foreach (var entry in data.Entries)
            {
                Check(data.AioValue(entry, bytes).Length > 0 && data.Description(entry).Length > 0, "campo consultavel " + entry.Field.Name);
                data.Hypothesis(entry, bytes);
            }
            Check(AioMap.Hex(bytes) == original && snapshot.Data["B1"] == original, "nenhum byte AIO alterado pela consulta");
            return count;
        }
    }
}
