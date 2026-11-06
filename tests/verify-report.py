"""Relatório observado em processos reais da CLI; sem dependências externas."""
import tempfile
from pathlib import Path
from importlib.util import module_from_spec, spec_from_file_location

SPEC = spec_from_file_location("strategy_terminal", Path(__file__).with_name("verify-strategy.py"))
S = module_from_spec(SPEC)
SPEC.loader.exec_module(S)
C = S.CAREER


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-report-") as folder:
        path = Path(folder) / "carreira.json"
        term = C.create(path)
        term.answer("Escolha: ", 6)
        term.answer("Histórico (", "t")
        term.expect("Etapa não disputada")
        term.answer("Histórico (", 0)
        S.plan(term, 1, 8, 29)
        C.race(term, 1)
        before = path.read_bytes()
        first = S.snapshot(path)["Rounds"][0]
        term.answer("Escolha: ", 6)
        term.answer("Histórico (", 1)
        term.answer("Histórico (", "t")
        term.expect("RELATÓRIO OBSERVADO POR TRECHOS")
        term.expect("Início: setores 1..7 | tempo")
        term.expect("Meio: setores 8..28 | tempo")
        term.expect("Final: setores 29..36 | tempo")
        term.expect("Sem estimar tempo alternativo")
        term.answer("Histórico (", 0)
        term.close()
        assert path.read_bytes() == before
        term = C.Terminal("carreira", "continuar", "--arquivo", str(path))
        C.prepare(term, 2); C.race(term, 2); term.close()
        assert S.snapshot(path)["Rounds"][0] == first
        print("OK: etapa futura sem fatos; relatório com três trechos, setores e eventos; consulta não grava e retomada avança.")
    logs = C.ROOT / "generated-artifacts/verification"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "report-terminal-transcript.txt").write_text("\n\n".join(C.TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    main()
