"""Fluxo real da CLI em pseudoterminal Linux; somente biblioteca padrão Python."""
import hashlib
import json
import os
import pty
import select
import subprocess
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
os.chdir(ROOT)
CLI = ["dotnet", str(ROOT / "src/Stintoria.Cli/bin/Debug/net8.0/Stintoria.Cli.dll")]
ENV = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", TERM="dumb")
TRANSCRIPTS = []


class Terminal:
    def __init__(self, *args):
        self.master, slave = pty.openpty()
        self.process = subprocess.Popen(CLI + list(args), stdin=slave, stdout=slave, stderr=slave,
                                        env=ENV, start_new_session=True)
        os.close(slave)
        self.buffer = b""
        self.transcript = bytearray()

    def expect(self, text):
        target = text.encode()
        deadline = time.monotonic() + 40
        while target not in self.buffer:
            if time.monotonic() > deadline:
                self.process.kill()
                raise AssertionError(f"Tempo esgotado esperando {text!r}: {self.buffer[-1500:]!r}")
            if select.select([self.master], [], [], 0.2)[0]:
                try:
                    chunk = os.read(self.master, 65536)
                except OSError:
                    chunk = b""
                if not chunk:
                    raise AssertionError(f"Terminal encerrou antes de {text!r}: {self.buffer[-1500:]!r}")
                self.buffer += chunk
                self.transcript.extend(chunk)
        end = self.buffer.index(target) + len(target)
        self.buffer = self.buffer[end:]

    def send(self, line):
        os.write(self.master, (str(line) + "\n").encode())

    def answer(self, prompt, line):
        self.expect(prompt)
        self.send(line)

    def close(self):
        self.answer("Escolha: ", 5)
        self.expect("Até a próxima!")
        assert self.process.wait(timeout=10) == 0
        os.close(self.master)
        TRANSCRIPTS.append(self.transcript.decode("utf-8", errors="replace"))


def create(path, invalid=False):
    term = Terminal("carreira", "nova", "--arquivo", str(path), "--seed", "42")
    if invalid:
        term.answer("Nome fictício: ", "")
        term.expect("Entrada inválida:")
        term.answer("Nome fictício: ", "123")
        term.expect("Entrada inválida:")
    term.answer("Nome fictício: ", "Ari Horizonte")
    if invalid:
        term.answer("Nacionalidade: ", "")
        term.expect("Entrada inválida:")
    term.answer("Nacionalidade: ", "Brasileira")
    if invalid:
        term.answer("Origem: ", 9)
        term.expect("Entrada inválida:")
    term.answer("Origem: ", 4)
    if invalid:
        term.answer("Equipe: ", 0)
        term.expect("Entrada inválida:")
    term.answer("Equipe: ", 2)
    term.expect("vaga de Ravi Silveira (p03)")
    if invalid:
        term.answer("Confirmar entrada? (s/n): ", "talvez")
        term.expect("Entrada inválida:")
    term.answer("Confirmar entrada? (s/n): ", "s")
    term.expect("Carreira criada e salva.")
    return term


def prepare(term, number, invalid=False, training=None):
    term.answer("Escolha: ", 1)
    if invalid:
        term.answer("Treino: ", 0)
        term.expect("Entrada inválida:")
    term.answer("Treino: ", training if training is not None else (2 if number <= 4 else 1))
    if invalid:
        term.answer("Ritmo: ", 9)
        term.expect("Entrada inválida:")
    term.answer("Ritmo: ", (number - 1) % 3 + 1)
    term.expect("Preparação salva. Você pode fechar e retomar antes de correr.")


def race(term, number):
    term.answer("Escolha: ", 2)
    term.expect("Etapa concluída e salva.")
    term.expect(f"RESULTADO DA ETAPA {number}/8")
    term.expect("[VOCÊ] Ari Horizonte (jogador)")


def snapshot(path):
    envelope = json.loads(path.read_text())
    return json.loads(envelope["Payload"])["Snapshot"]


def command(*args, expected=0, input_text=None):
    result = subprocess.run(CLI + list(args), input=input_text, capture_output=True, text=True,
                            env=ENV, timeout=60)
    assert result.returncode == expected, (result.returncode, result.stdout, result.stderr)
    return result


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-career-pty-") as folder:
        folder = Path(folder)
        source, full = folder / "parcial.json", folder / "continuo.json"
        export, resumed = folder / "exportado.json", folder / "retomado.json"
        cancel = folder / "cancelado.json"
        command("carreira", "nova", "--arquivo", str(cancel), input_text="Ari Horizonte\nBrasileira\n1\n1\nn\n")
        assert not cancel.exists()
        print("OK: cancelar a criação não cria save.")

        term = create(source, invalid=True)
        term.answer("Escolha: ", "abc")
        term.expect("Opção inválida.")
        term.answer("Escolha: ", 2)
        term.expect("Decisão rejeitada: Prepare a etapa antes de correr.")
        prepare(term, 1, invalid=True)
        term.close()
        pending = snapshot(source)
        assert pending["Profile"]["Age"] == 14
        assert pending["Profile"]["Name"] == "Ari Horizonte"
        assert pending["Profile"]["Nationality"] == "Brasileira"
        assert pending["Profile"]["Origin"] == "SimRacer"
        assert pending["Profile"]["TeamId"] == "t02"
        assert pending["Profile"]["DisplacedDriverId"] == "p03"
        assert pending["Skill"] == 72 and pending["TrainingCredits"] == 3
        assert pending["Rounds"] == [] and pending["Pending"]["Pace"] == "Conservar"
        print("OK: inputs inválidos corrigidos no terminal; perfil e preparação salvos antes da corrida.")

        term = Terminal("carreira", "continuar", "--arquivo", str(source))
        term.expect("Ari Horizonte (jogador) | 14 anos | Brasileira")
        before = source.read_bytes()
        term.answer("Escolha: ", 1)
        term.expect("Decisão rejeitada: Etapa já preparada")
        assert source.read_bytes() == before
        race(term, 1)
        for number in (2, 3):
            prepare(term, number)
            race(term, number)
        prepare(term, 4)
        term.close()
        assert len(snapshot(source)["Rounds"]) == 3 and snapshot(source)["Pending"]["Round"] == 4
        assert snapshot(source)["TrainingCredits"] == 0
        prior_bytes = source.read_bytes()
        prior_history = snapshot(source)["Rounds"]
        print("OK: fechamento e retomada; três provas completas e quarta preparada, sem cobrar treino duplicado.")

        command("carreira", "exportar", "--arquivo", str(source), "--destino", str(export))
        command("carreira", "importar", "--origem", str(export), "--arquivo", str(resumed))
        assert source.read_bytes() == export.read_bytes() == resumed.read_bytes()
        # EOF no meio da escolha: nenhuma preparação parcial pode ser aplicada.
        eof_path = folder / "eof.json"
        command("carreira", "nova", "--arquivo", str(eof_path),
                input_text="Ari Horizonte\nBrasileira\n1\n1\ns\n5\n")
        eof_before = eof_path.read_bytes()
        command("carreira", "continuar", "--arquivo", str(eof_path), input_text="1\n2\n", expected=1)
        assert eof_before == eof_path.read_bytes()
        print("OK: exportação/importação e fechamento no meio da entrada preservam o checkpoint.")

        term = create(full)
        for number in range(1, 9):
            prepare(term, number)
            race(term, number)
        term.expect("Campeões:")
        term.close()

        term = Terminal("carreira", "continuar", "--arquivo", str(resumed))
        race(term, 4)
        before = resumed.read_bytes()
        term.answer("Escolha: ", 1)
        term.answer("Treino: ", 2)
        term.answer("Ritmo: ", 2)
        term.expect("Decisão rejeitada: Créditos de treino esgotados.")
        assert resumed.read_bytes() == before
        for number in range(5, 9):
            prepare(term, number)
            race(term, number)
        term.expect("Campeões:")
        before = resumed.read_bytes()
        term.answer("Escolha: ", 2)
        term.expect("Decisão rejeitada: Carreira Kart encerrada")
        assert resumed.read_bytes() == before
        term.close()
        assert full.read_bytes() == resumed.read_bytes()
        assert source.read_bytes() == prior_bytes
        final = snapshot(resumed)
        assert final["Finished"] and len(final["Rounds"]) == 8
        assert final["Rounds"][:3] == prior_history
        assert sum(r["Points"] for r in final["DriverStandings"]) == 808
        assert sum(r["Points"] for r in final["TeamStandings"]) == 808
        for round_data in final["Rounds"]:
            ids = [r["DriverId"] for r in round_data["Race"]["Result"]["Classification"]]
            assert len(ids) == 20 and ids.count("jogador") == 1 and "p03" not in ids
        assert final["TrainingCredits"] == 0 and final["Skill"] == 78
        print("OK: oito etapas; jogador identificado; recursos limitados; histórico anterior intacto.")
        print("OK: carreira contínua e retomada idênticas byte a byte.")
        print("SHA-256:", hashlib.sha256(full.read_bytes()).hexdigest())
        print("OK: fluxo real em pseudoterminal concluído; nenhuma interface 3D ou serviço externo usado.")
    log_dir = ROOT / "generated-artifacts/verification"
    log_dir.mkdir(parents=True, exist_ok=True)
    (log_dir / "career-terminal-transcript.txt").write_text("\n\n".join(TRANSCRIPTS), encoding="utf-8")


if __name__ == "__main__":
    main()
