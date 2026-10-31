using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AioAssist
{
    internal static class AioOfflineViewTests
    {
        private static int count;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("FALHOU offline PT: " + message); count++; }
        public static int Run()
        {
            count = 0;
            var model = new AioOfflineModel();
            var snapshot = OfflineData.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "examples", "leitura-anonimizada.json"));
            string original = snapshot.Data["B1"];
            model.SetCurrent(snapshot);
            Check(model.Rows.Count == 292, "292 parametros da leitura real decodificados");
            Check(snapshot.Summary.Contains("2026-10-17T19:53:34"), "data historica da captura preservada");
            Check(model.Rows.Single(r => r.Field.Name == "MANUFACTURER_S_BRAND").Meaning == "Citroën", "marca traduzida");
            Check(model.Rows.Single(r => r.Field.Name == "TYPE_AIR_CONDITIONING_MIXING").Meaning == "Uma zona", "climatizacao monozona traduzida");
            Check(model.Rows.Single(r => r.Field.Name == "TYPE_AIR_CONDITIONING").Meaning == "Ar-condicionado simples", "tipo simples traduzido");
            Check(model.Rows.Single(r => r.Field.Name == "AMBIANCE_DEFAULT_000").Meaning.Contains("nome visual não informado"), "tema sem nome inventado");
            Check(model.Rows.Single(r => r.Field.Name == "AMBIANCE_DEFAULT_000").Raw == "2 (0x2)", "valor bruto decimal e hexadecimal");
            Check(model.Rows.All(r => r.Difference == "Sem comparação"), "ausencia de referencia nao tratada como zero");
            Check(model.Rows.All(r => !string.IsNullOrEmpty(r.Meaning) && !string.IsNullOrEmpty(r.Name)), "todos os campos com valor ou limite explicito");
            foreach (var f in model.Map.Fields)
                foreach (var raw in f.Choices.Keys)
                    Check(!model.Translations.Value(f, raw).EndsWith(" (original)"), "traducao da opcao: " + f.Name + "/" + raw);
            model.SetReference(snapshot);
            Check(model.Rows.All(r => !r.Changed) && model.ChangedBytes == 0, "comparacao identica nao inventa diferencas");
            var changed = new Snapshot { Name = "tema-alterado.json" };
            foreach (var pair in snapshot.Data) changed.Data.Add(pair.Key, pair.Value);
            changed.Data["B1"] = AioMap.Hex(model.Map.Apply(AioMap.Bytes(original), new Dictionary<string, ulong> { { "AMBIANCE_DEFAULT_000", 3 } }));
            model.SetReference(changed);
            Check(model.Rows.Count(r => r.Changed) == 1 && model.ChangedBytes == 1, "mudanca de tema gera um campo e um byte diferente");
            Check(model.Rows.Single(r => r.Changed).Reference.StartsWith("3 — Código de tema 3"), "outra leitura traduzida corretamente");
            Check(snapshot.Data["B1"] == original, "consulta nao altera dados base");
            string csv = model.Csv(model.Rows.Where(r => r.Changed));
            Check(csv.Contains("AMBIANCE_DEFAULT_000") && csv.Split('\n').Length == 3, "CSV reflete filtro e inclui identificador");
            var invalid = new Snapshot(); invalid.Data.Add("B1", "00"); bool rejected = false;
            try { model.SetCurrent(invalid); } catch (FormatException) { rejected = true; }
            Check(rejected && model.Current == snapshot && model.Reference == changed, "arquivo invalido nao substitui base valida");
            model.SetCurrent(snapshot);
            Check(model.Reference == null && model.Rows.All(r => !r.Changed), "trocar base limpa comparacao anterior");
            Check(AioTranslations.Category(model.Map.Fields.Single(f => f.Name == "AMBIANCE_DEFAULT_000")) == "Temas", "categoria de temas");
            Check(AioTranslations.Category(model.Map.Fields.Single(f => f.Name == "TYPE_AIR_CONDITIONING")) == "Climatização", "categoria de climatizacao");
            return count;
        }
    }
}
