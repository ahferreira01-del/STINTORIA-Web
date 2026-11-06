"""Temporada mista com IA tática em terminal real e saves v1/v2/v3."""
import hashlib
import json
import tempfile
from pathlib import Path
from importlib.util import module_from_spec, spec_from_file_location

SPEC = spec_from_file_location("strategy_terminal", Path(__file__).with_name("verify-strategy.py"))
S = module_from_spec(SPEC); SPEC.loader.exec_module(S)
C = S.CAREER


def start(term):
    C.prepare(term, 1); C.race(term, 1)
    S.plan(term, 2); C.race(term, 2)


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-ai-pty-") as folder:
        folder = Path(folder); source = folder / "original.json"
        term = C.create(source); initial = source.read_bytes()
        S.plan(term, 1, confirm="n", menu=8)
        assert source.read_bytes() == initial
        start(term)
        previous = S.snapshot(source)["Rounds"][:]
        S.plan(term, 3, 8, 29, menu=8)
        pending = source.read_bytes()
        assert json.loads(source.read_text())["Format"] == "stintoria-carreira-v3"
        plans = S.payload(source)["Pending"]["Tactics"]["Plans"]
        assert len(plans) == 19 and len({p["DriverId"] for p in plans}) == 19
        term.answer("Escolha: ", 8); term.expect("Etapa já preparada; não é possível substituir")
        assert source.read_bytes() == pending
        term.close()
        print("OK: modo IA explícito, cancelamento sem gasto, 19 planos persistidos; provas v1/v2 intactas.")
        exported, resumed = folder / "exportado.json", folder / "retomado.json"
        C.command("carreira", "exportar", "--arquivo", str(source), "--destino", str(exported))
        C.command("carreira", "importar", "--origem", str(exported), "--arquivo", str(resumed))
        assert resumed.read_bytes() == pending
        term = C.Terminal("carreira", "continuar", "--arquivo", str(resumed))
        term.expect("planos de 19 rivais já registrados.")
        C.race(term, 3)
        round_ = S.snapshot(resumed)["Rounds"][2]
        assert len(round_["Race"]["Decisions"]) == 60
        before_query = resumed.read_bytes()
        term.answer("Escolha: ", 6)
        term.answer("Histórico (", 3)
        term.expect("PLANO IA p01:")
        term.expect("Decisão pré-corrida: Perfil Prudente")
        term.answer("Histórico (", "t p01")
        term.expect("RELATÓRIO OBSERVADO POR TRECHOS")
        term.expect("Caio Valença")
        term.expect("Final: setores 25..36")
        term.answer("Histórico (", 0)
        assert resumed.read_bytes() == before_query
        for number in range(4, 9):
            S.plan(term, number, 8, 29, menu=8); C.race(term, number)
        term.close()
        final = S.snapshot(resumed)
        assert final["Rounds"][:2] == previous and final["Finished"]
        assert sum(r["Points"] for r in final["DriverStandings"]) == 808
        assert all(len(r["Race"]["Decisions"]) == 60 for r in final["Rounds"][2:])
        assert source.read_bytes() == pending
        print("OK: exportação/importação, retomada, 60 comandos por prova tática e consulta causal dos rivais.")
        full = folder / "continuo.json"; term = C.create(full); start(term)
        for number in range(3, 9):
            S.plan(term, number, 8, 29, menu=8); C.race(term, number)
        term.close()
        assert full.read_bytes() == resumed.read_bytes()
        print("OK: oito provas; temporada mista contínua/retomada idêntica byte a byte, sem reatribuir resultados.")
        print("SHA-256:", hashlib.sha256(full.read_bytes()).hexdigest())
    logs = C.ROOT / "generated-artifacts/verification"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "ai-terminal-transcript.txt").write_text("\n\n".join(C.TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    main()
