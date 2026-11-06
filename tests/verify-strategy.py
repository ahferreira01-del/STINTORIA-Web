"""Estratégia no terminal real: confirmação, validação, legado, replay e retomada."""
import hashlib
import json
import subprocess
import tempfile
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

SPEC = spec_from_file_location("career_terminal", Path(__file__).with_name("verify-career.py"))
CAREER = module_from_spec(SPEC)
SPEC.loader.exec_module(CAREER)


def payload(path):
    return json.loads(json.loads(path.read_text())["Payload"])


def snapshot(path):
    value = payload(path)["Snapshot"]
    return value.get("Career", value)


def plan(term, number, middle=13, final=25, confirm="s", invalid=False, defaults=False, menu=7):
    term.answer("Escolha: ", menu)
    term.answer("Treino do plano: ", 2 if number <= 4 else 1)
    if invalid:
        for value in ("abc", "1", "36"):
            term.answer("Primeiro setor do meio", value)
            term.expect("Entrada inválida:")
    term.answer("Primeiro setor do meio", "" if defaults else middle)
    if invalid:
        for value in (middle, "37"):
            term.answer("Primeiro setor do final", value)
            term.expect("Entrada inválida:")
    term.answer("Primeiro setor do final", "" if defaults else final)
    for prompt, pace in (("Ritmo do início: ", 1), ("Ritmo do meio: ", 2), ("Ritmo do final: ", 3)):
        if invalid:
            term.answer(prompt, 0)
            term.expect("Entrada inválida:")
        term.answer(prompt, pace)
    term.expect("PLANO DE TRÊS TRECHOS")
    term.expect(f"Início: setores 1..{middle - 1} — CONSERVAR")
    term.expect(f"Meio: setores {middle}..{final - 1} — EQUILIBRADO")
    term.expect(f"Final: setores {final}..36 — ATACAR")
    term.expect("Previsão qualitativa:")
    if invalid:
        term.answer("Confirmar plano? (s/n): ", "talvez")
        term.expect("Entrada inválida:")
    term.answer("Confirmar plano? (s/n): ", confirm)
    term.expect("Plano confirmado e salvo." if confirm == "s" else "Plano cancelado.")


def first_legacy(term):
    CAREER.prepare(term, 1)
    CAREER.race(term, 1)


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-strategy-pty-") as folder:
        folder = Path(folder)
        source = folder / "origem.json"
        term = CAREER.create(source)
        before = source.read_bytes()
        plan(term, 1, confirm="n", invalid=True, defaults=True)
        assert source.read_bytes() == before
        term.close()
        eof = subprocess.run(CAREER.CLI + ["carreira", "continuar", "--arquivo", str(source)],
                             input="7\n2\n13\n25\n1\n2\n3\n", capture_output=True, text=True,
                             env=CAREER.ENV, timeout=60)
        assert eof.returncode == 1 and source.read_bytes() == before
        print("OK: limites, ritmos e confirmação inválidos tratados; cancelamento e EOF não gastam nem gravam.")

        term = CAREER.Terminal("carreira", "continuar", "--arquivo", str(source))
        first_legacy(term)
        previous = snapshot(source)["Rounds"][0]
        plan(term, 2, 8, 29)
        pending_bytes = source.read_bytes()
        assert json.loads(source.read_text())["Format"] == "stintoria-carreira-v2"
        assert snapshot(source)["TrainingCredits"] == 2
        assert payload(source)["Pending"]["Strategy"]["MiddleStartSector"] == 8
        term.answer("Escolha: ", 7)
        term.expect("Etapa já preparada; não é possível substituir o plano")
        assert source.read_bytes() == pending_bytes
        term.close()
        print("OK: etapa legada preservada; plano confirmado com fronteiras 8/29 salvo sem duplicação de treino.")

        exported, resumed = folder / "exportado.json", folder / "retomado.json"
        CAREER.command("carreira", "exportar", "--arquivo", str(source), "--destino", str(exported))
        CAREER.command("carreira", "importar", "--origem", str(exported), "--arquivo", str(resumed))
        assert source.read_bytes() == exported.read_bytes() == resumed.read_bytes()
        term = CAREER.Terminal("carreira", "continuar", "--arquivo", str(resumed))
        term.expect("Início: setores 1..7 — CONSERVAR")
        CAREER.race(term, 2)
        round_two = snapshot(resumed)["Rounds"][1]["Race"]
        assert [d["Sector"] for d in round_two["Decisions"]] == [1, 8, 29]
        assert [e["Step"] for e in round_two["Result"]["Events"]
                if e["DriverId"] == "jogador" and e["Code"] == "ritmo"] == [1, 8, 29]
        # Consulta causal exibe plano e comandos da corrida já disputada.
        term.answer("Escolha: ", 6)
        term.answer("Histórico (", 2)
        term.expect("PLANO DE TRÊS TRECHOS")
        term.expect("DECISÕES REGISTRADAS")
        term.expect("Setor 8: [VOCÊ] Ari Horizonte (jogador) — equilibrado")
        term.answer("Histórico (", 0)
        for number in range(3, 9):
            if number == 5:
                checkpoint = resumed.read_bytes()
                term.answer("Escolha: ", 7)
                term.answer("Treino do plano: ", 2)
                term.expect("Créditos de treino esgotados.")
                assert resumed.read_bytes() == checkpoint
            plan(term, number, defaults=True)
            CAREER.race(term, number)
        term.close()
        final = snapshot(resumed)
        assert final["Finished"] and len(final["Rounds"]) == 8
        assert final["Rounds"][0] == previous and source.read_bytes() == pending_bytes
        assert sum(r["Points"] for r in final["DriverStandings"]) == 808
        assert sum(r["Points"] for r in final["TeamStandings"]) == 808
        print("OK: exportação/importação, retomada, comandos nos setores corretos e histórico causal; oito provas.")

        full = folder / "continuo.json"
        term = CAREER.create(full)
        first_legacy(term)
        for number in range(2, 9):
            plan(term, number, 8 if number == 2 else 13, 29 if number == 2 else 25)
            CAREER.race(term, number)
        term.close()
        assert full.read_bytes() == resumed.read_bytes()
        print("OK: campeonato misto contínuo e retomado idênticos byte a byte; histórico anterior intacto.")
        print("SHA-256:", hashlib.sha256(full.read_bytes()).hexdigest())
    logs = CAREER.ROOT / "generated-artifacts/verification"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "strategy-terminal-transcript.txt").write_text("\n\n".join(CAREER.TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    main()
