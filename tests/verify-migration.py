"""Fluxo real de migração no terminal; biblioteca padrão, sem Unity ou serviços."""
import hashlib
import os
import subprocess
import tempfile
from pathlib import Path
from importlib.util import module_from_spec, spec_from_file_location

SPEC = spec_from_file_location("strategy_terminal", Path(__file__).with_name("verify-strategy.py"))
S = module_from_spec(SPEC); SPEC.loader.exec_module(S)
C = S.CAREER


def finish(term, expected, code=0):
    term.expect(expected)
    assert term.process.wait(timeout=15) == code
    os.close(term.master)
    C.TRANSCRIPTS.append(term.transcript.decode("utf-8", errors="replace"))


def command(*args, code=0, input_text=None):
    result = subprocess.run(C.CLI + list(args), input=input_text, capture_output=True, text=True, env=C.ENV, timeout=45)
    assert result.returncode == code, result.stdout + result.stderr
    return result.stdout + result.stderr


def main():
    with tempfile.TemporaryDirectory(prefix="stintoria-migration-pty-") as temp:
        root = Path(temp); source = root / "carreira original.json"; slots = root / "slots Unity"
        term = C.create(source)
        C.prepare(term, 1); C.race(term, 1)
        S.plan(term, 2, menu=8); term.close()
        original = source.read_bytes()
        args = ("carreira", "importar-unity", "--arquivo", str(source), "--slots", str(slots), "--slot", "2")
        term = C.Terminal(*args)
        term.expect("Piloto: Ari Horizonte | ID: jogador | 14 anos | Brasileira")
        term.expect("Temporada Kart: 1/8 etapas concluídas; seed 42.")
        term.expect("preparação pendente: sim")
        term.expect("stintoria-carreira-v3")
        term.expect(str(slots / "carreira-2.stint"))
        term.answer("Para confirmar, digite IMPORTAR 2", "IMPORTAR 1")  # confirmação para outro slot não vale
        finish(term, "Importação cancelada.")
        assert not slots.exists() and source.read_bytes() == original
        output = command(*args, input_text="")  # EOF não confirma
        assert "Importação cancelada." in output and not slots.exists()
        term = C.Terminal(*args)
        term.answer("Para confirmar, digite IMPORTAR 2", "IMPORTAR 2")
        finish(term, "Importação concluída no slot 2")
        checkpoint = (slots / "carreira-2.stint").read_bytes()
        assert source.read_bytes() == original and len(list(slots.glob("*.stint"))) == 1
        output = command(*args, code=1, input_text="IMPORTAR 2\n")
        assert "Slot ocupado" in output and "Para confirmar" not in output
        assert (slots / "carreira-2.stint").read_bytes() == checkpoint
        assert source.read_bytes() == original
        for slot in ("0", "4", "abc"):
            output = command("carreira", "importar-unity", "--arquivo", str(source),
                             "--slots", str(slots), "--slot", slot, code=1)
            assert "Slot deve ser 1, 2 ou 3" in output
        bad = root / "corrompido.json"; bad.write_text('{"Format":"desconhecido"}')
        command("carreira", "importar-unity", "--arquivo", str(bad), "--slots", str(slots), "--slot", "1", code=1)
        assert not (slots / "carreira-1.stint").exists()
        # Mudança entre prévia e confirmação: não importar a escolha que não foi consentida.
        args1 = args[:-1] + ("1",)
        term = C.Terminal(*args1)
        term.expect("Para confirmar, digite IMPORTAR 1")
        source.write_bytes(original + b"\n")
        term.send("IMPORTAR 1")
        finish(term, "O JSON mudou desde a prévia", code=1)
        assert source.read_bytes() == original + b"\n" and not (slots / "carreira-1.stint").exists()
        # Restaurar para verificar que o terminal original continua jogável sem alteração de resultados.
        source.write_bytes(original)
        term = C.Terminal("carreira", "continuar", "--arquivo", str(source))
        term.expect("planos de 19 rivais já registrados.")
        C.race(term, 2); term.close()
        assert len(S.snapshot(source)["Rounds"]) == 2
        assert (slots / "carreira-2.stint").read_bytes() == checkpoint
        print("OK: prévia real, confirmação exata, cancelamento/EOF, conflito, corrupção, entradas e origem alterada.")
        print("OK: JSON preservado pela migração; carreira original retomada, checkpoint independente.")
        print("MIGRAÇÃO PTY SHA-256:", hashlib.sha256(checkpoint).hexdigest())
        folder = C.ROOT / "generated-artifacts/verification"; folder.mkdir(parents=True, exist_ok=True)
        (folder / "migration-terminal-transcript.txt").write_text("\n\n".join(C.TRANSCRIPTS))


if __name__ == "__main__":
    main()
