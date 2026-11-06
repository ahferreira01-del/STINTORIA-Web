"""Navegação real do histórico: CLI C#, PTY Linux, sem dependências externas."""
import hashlib
import subprocess
import tempfile
from pathlib import Path

from importlib.util import module_from_spec, spec_from_file_location

SPEC = spec_from_file_location("career_terminal", Path(__file__).with_name("verify-career.py"))
CAREER = module_from_spec(SPEC)
SPEC.loader.exec_module(CAREER)
PROMPT = "Histórico (1-8, a anterior, p próxima, r resumo, e evolução, 0 voltar): "


def browse(term, path, completed, pending=False):
    before = path.read_bytes()
    start = len(term.transcript)
    term.answer("Escolha: ", 6)
    term.expect(f"RESUMO DO CAMPEONATO — {completed}/8 etapas disputadas")
    for invalid in ("texto", "9", "-1"):
        term.answer(PROMPT, invalid)
        term.expect("Etapa inválida.")
    term.answer(PROMPT, 1)
    term.expect("HISTÓRICO — ETAPA 1/8")
    if completed:
        term.expect("CLASSIFICAÇÃO FINAL DA CORRIDA")
        term.expect("[VOCÊ] Ari Horizonte (jogador)")
        term.expect("PONTOS ACUMULADOS DE EQUIPES")
        term.expect("DECISÕES REGISTRADAS")
        term.expect("ACONTECIMENTOS")
    else:
        term.expect("Sem resultado, pontos ou acontecimentos de corrida registrados")
    term.answer(PROMPT, "a")
    term.expect("Etapa inválida.")
    term.answer(PROMPT, "p")
    term.expect("HISTÓRICO — ETAPA 2/8")
    term.answer(PROMPT, "a")
    term.expect("HISTÓRICO — ETAPA 1/8")
    term.answer(PROMPT, 8)
    term.expect("HISTÓRICO — ETAPA 8/8")
    term.answer(PROMPT, "p")
    term.expect("Etapa inválida.")
    if pending:
        term.answer(PROMPT, 4)
        term.expect("preparada, ainda não disputada")
        term.expect("Preparação registrada: treino técnico")
        term.expect("Sem resultado, pontos ou acontecimentos de corrida registrados")
    # Acesso direto a cada uma das oito etapas.
    for number in range(1, 9):
        term.answer(PROMPT, number)
        term.expect(f"HISTÓRICO — ETAPA {number}/8")
    term.answer(PROMPT, "e")
    term.expect("EVOLUÇÃO DOS PONTOS")
    term.expect("EQUIPES")
    term.answer(PROMPT, "r")
    term.expect(f"RESUMO DO CAMPEONATO — {completed}/8 etapas disputadas")
    if completed:
        term.expect("Campeões:" if completed == 8 else "Líderes provisórios:")
    term.answer(PROMPT, 0)
    term.expect("CARREIRA — Ari Horizonte")
    assert path.read_bytes() == before, "Consultar histórico alterou os bytes do save"
    segment = term.transcript[start:].decode("utf-8", errors="replace")
    if completed == 8:
        # Matriz de todos os pilotos/equipes conferida contra resultados salvos,
        # e não contra totais produzidos pela própria consulta.
        snapshot = CAREER.snapshot(path)
        for team in (False, True):
            entities = snapshot["TeamStandings" if team else "DriverStandings"]
            for entity in entities:
                total, totals = 0, []
                for round_ in snapshot["Rounds"]:
                    for result in round_["Race"]["Result"]["Classification"]:
                        member = next(d for d in snapshot["Drivers"] if d["Id"] == result["DriverId"])
                        if (member["TeamId"] if team else result["DriverId"]) == entity["Id"]:
                            total += result["Points"]
                    totals.append(str(total))
                assert f"({entity['Id']}) | " + " | ".join(totals) in segment
        assert "Vencedor:" in segment and "Campeões:" in segment
    return before


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-history-pty-") as folder:
        path = Path(folder) / "carreira.json"
        term = CAREER.create(path)
        browse(term, path, 0)
        print("OK: carreira vazia; oito etapas sem resultados inventados; entradas e limites validados.")
        for number in range(1, 4):
            CAREER.prepare(term, number)
            CAREER.race(term, number)
        CAREER.prepare(term, 4)
        partial = browse(term, path, 3, pending=True)
        term.close()
        print("OK: navegação direta/anterior/próxima, preparação pendente e consulta sem mudar save.")
        term = CAREER.Terminal("carreira", "continuar", "--arquivo", str(path))
        browse(term, path, 3, pending=True)
        assert path.read_bytes() == partial
        CAREER.race(term, 4)
        for number in range(5, 9):
            CAREER.prepare(term, number)
            CAREER.race(term, number)
        final = browse(term, path, 8)
        term.close()
        print("OK: retomada após consulta; oito resultados e matrizes de todos os pilotos/equipes conferidos.")
        # EOF no submenu não grava, não bloqueia e retorna encerramento limpo.
        result = subprocess.run(CAREER.CLI + ["carreira", "continuar", "--arquivo", str(path)],
                                input="6\n1\n", text=True, capture_output=True, env=CAREER.ENV, timeout=60)
        assert result.returncode == 0 and "Até a próxima!" in result.stdout
        assert path.read_bytes() == final
        print("OK: fechamento no submenu preserva arquivo; histórico completo não altera resultados.")
        print("SHA-256 final preservado:", hashlib.sha256(final).hexdigest())
    logs = CAREER.ROOT / "generated-artifacts/verification"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "history-terminal-transcript.txt").write_text("\n\n".join(CAREER.TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    main()
