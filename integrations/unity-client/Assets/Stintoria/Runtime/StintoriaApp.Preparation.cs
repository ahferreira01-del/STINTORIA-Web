using System;
using System.Globalization;
using Stintoria.Core;
using UnityEngine;

namespace Stintoria.UnityClient
{
    public sealed partial class StintoriaApp
    {
        private void Preparation()
        {
            GUILayout.Label("PREPARAÇÃO — PRÓXIMA ETAPA");
            if (draft == null) {
                training = GUILayout.SelectionGrid(training, new[] {"Descansar (grátis)", "Treino técnico (1 crédito)"}, 2);
                GUILayout.Label("Primeiro setor do meio (2..35)"); middleStart = GUILayout.TextField(middleStart, 2);
                GUILayout.Label("Primeiro setor do final (maior que o meio, até 36)"); finalStart = GUILayout.TextField(finalStart, 2);
                var choices = new[] {"CONSERVAR", "EQUILIBRADO", "ATACAR"};
                GUILayout.Label("Início"); firstPace = GUILayout.SelectionGrid(firstPace, choices, 3);
                GUILayout.Label("Meio"); middlePace = GUILayout.SelectionGrid(middlePace, choices, 3);
                GUILayout.Label("Final"); lastPace = GUILayout.SelectionGrid(lastPace, choices, 3);
                tactical = GUILayout.Toggle(tactical, "IA tática para os 19 rivais (mesmas regras; modo explícito)");
                if (GUILayout.Button("Revisar plano sem gastar"))
                    Action(() => {
                        if (!int.TryParse(middleStart, NumberStyles.None, CultureInfo.InvariantCulture, out var middle) ||
                            !int.TryParse(finalStart, NumberStyles.None, CultureInfo.InvariantCulture, out var final))
                            throw new ArgumentException("Os limites dos trechos devem ser números inteiros.");
                        var proposed = new RaceStrategy((Pace)firstPace, (Pace)middlePace, (Pace)lastPace, middle, final);
                        client.Preview((Training)training, proposed, tactical);
                        draft = proposed; message = "";
                    });
            }
            else {
                GUILayout.Label("CONFIRMAÇÃO — nada foi gasto ou salvo pela prévia");
                GUILayout.Label("Início 1.." + (draft.MiddleStartSector - 1) + ": " + draft.Beginning);
                GUILayout.Label("Meio " + draft.MiddleStartSector + ".." + (draft.FinalStartSector - 1) + ": " + draft.Middle);
                GUILayout.Label("Final " + draft.FinalStartSector + "..36: " + draft.Final);
                GUILayout.Label("Conservar: menor termo de velocidade, desgaste e risco. Atacar: mais velocidade no termo de ritmo," +
                    " maior desgaste e risco. Equilibrado: intermediário. Clima e erros também afetam a prova; sem vitória garantida.");
                GUILayout.Label(training == 1 ? "Treino técnico: custa 1 crédito e dá +2 habilidade." : "Descanso gratuito.");
                GUILayout.Label(tactical ? "19 planos IA serão registrados antes da largada." : "Rivais mantêm o modo anterior.");
                if (GUILayout.Button("Confirmar plano e salvar"))
                    ActionAsync(async () => { await client.PrepareAsync((Training)training, draft, tactical); draft = null; Go(Page.Hub); });
                if (GUILayout.Button("Voltar e editar")) draft = null;
            }
            if (GUILayout.Button("Cancelar sem gastar")) { draft = null; Go(Page.Hub); }
        }
    }
}
