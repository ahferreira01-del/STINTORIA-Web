#!/usr/bin/env python3
"""Fluxo real em PTY, biblioteca padrão; nenhum serviço externo."""
import hashlib
import json
import tempfile
from pathlib import Path
from importlib.util import spec_from_file_location, module_from_spec

spec = spec_from_file_location("career_terminal", Path(__file__).with_name("verify-career.py"))
helper = module_from_spec(spec)
spec.loader.exec_module(helper)


def payload(path):
    return json.loads(json.loads(path.read_text())["Payload"])


def start(path):
    term = helper.create(path)
    helper.prepare(term, 1, training=1)
    term.answer("Escolha: ", 9)
    term.expect("Percorridos 0/36")
    term.expect("Corrida> ")
    return term


def advance(term, command):
    term.send(command)
    term.expect("Checkpoint salvo.")
    term.expect("Corrida> ")


def run():
    with tempfile.TemporaryDirectory(prefix="stintoria-live-pty-") as directory:
        full = Path(directory) / "continua.json"
        resumed = Path(directory) / "retomada.json"
        for path in (full, resumed):
            term = start(path)
            before = path.read_bytes()
            for command in ("avancar 0", "avancar 4", "avancar 1 turbo", "box", "avancar"):
                term.send(command)
                term.expect("inválid")
                term.expect("Corrida> ")
                assert path.read_bytes() == before
            term.send("estado")
            term.expect("Percorridos 0/36")
            term.expect("Corrida> ")
            advance(term, "avancar 3 atacar")
            checkpoint = payload(path)
            assert checkpoint["Active"]["Sector"] == 3 and len(checkpoint["Rounds"]) == 0
            assert checkpoint["Active"]["Decisions"] == [{"Sector": 1, "Pace": "Atacar"}]
            if path == resumed:
                saved = path.read_bytes()
                term.send("sair")
                term.expect("Corrida em andamento salva.")
                term.close()
                assert path.read_bytes() == saved
                term = helper.Terminal("carreira", "continuar", "--arquivo", str(path))
                term.answer("Escolha: ", 2)
                term.expect("Corrida em andamento; avance pelos setores.")
                assert path.read_bytes() == saved
                term.answer("Escolha: ", 9)
                term.expect("Percorridos 3/36")
                term.expect("Corrida> ")
            advance(term, "avancar 3 conservar")
            for _ in range(9):  # 6 -> 33
                advance(term, "avancar 3")
            advance(term, "avancar 2")  # setor 35
            checkpoint = payload(path)
            assert len(checkpoint["Rounds"]) == 0 and checkpoint["Active"]["Sector"] == 35
            assert all(row["Points"] == 0 for row in checkpoint["Snapshot"]["Career"]["DriverStandings"])
            before = path.read_bytes()
            term.send("avancar 2")
            term.expect("sem ultrapassar a bandeirada")
            term.expect("Corrida> ")
            assert path.read_bytes() == before
            term.send("avancar 1")
            term.expect("Bandeirada: etapa concluída e pontos salvos uma única vez.")
            term.close()
            checkpoint = payload(path)
            assert "Active" not in checkpoint and len(checkpoint["Rounds"]) == 1
            assert checkpoint["Rounds"][0]["Live"] == [{"Sector": 1, "Pace": "Atacar"}, {"Sector": 4, "Pace": "Conservar"}]
            assert json.loads(path.read_text())["Format"] == "stintoria-carreira-v4"
        assert full.read_bytes() == resumed.read_bytes()
        print("OK: PTY real, entradas inválidas, consulta, ritmo no próximo setor, blocos, bandeirada e pontos.")
        print("OK: fechamento no setor 3, rejeição de corrida automática, retomada até setor 36 idêntica byte a byte.")
        print("CARREIRA PARTICIPATIVA SHA-256:", hashlib.sha256(full.read_bytes()).hexdigest())
    out = helper.ROOT / "generated-artifacts/verification"
    out.mkdir(parents=True, exist_ok=True)
    (out / "participatory-terminal-transcript.txt").write_text("\n\n".join(helper.TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    run()
