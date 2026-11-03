"""Exporta somente transicoes CAN de clima das minhas capturas privadas.

Uso: python tools/extract_climate_evidence.py PASTA_DOS_LOGS
Nao exporta cabecalhos, porta COM, caminhos locais, marcas pessoais ou IDs VIN.
As capturas completas nao acompanham a publicacao; SHA-256 permite rastreio.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

FILES = {
    'rt6_sem_aio': '20261101-145727-958-rt6.log',
    'cactus': '20261101-150407-192-cactus.log',
    'rt6_com_aio_passageiro_repeticao': '20261101-151132-756-rt6_com_aio.log',
    'aio_sem_fachada': '20261101-151855-520-aio_sem_fachada.log',
    'rt6_desembacador': '20261101-183521-395-rt6_desembacador.log',
}
ALLOWED = {0x350, 0x364, 0x227}
FRAME = re.compile(r'(\d+),([0-9A-F]+),([01]),([01]),(\d+),([0-9A-F]*)')


def extract(path):
    previous = {}
    transitions = []
    total = 0
    selected = 0
    pulses = []
    led_changes = []
    pulse_start = None
    last_led = None
    for row in path.read_text(encoding='utf-8-sig').splitlines():
        match = FRAME.fullmatch(row)
        if not match:
            continue
        ms, ident, ext, rtr, dlc, payload = match.groups()
        if ext != '0' or rtr != '0' or len(payload) != int(dlc) * 2:
            continue
        total += 1
        ident = int(ident, 16)
        if ident not in ALLOWED:
            continue
        selected += 1
        data = bytes.fromhex(payload)
        ms = int(ms)
        if previous.get(ident) != payload:
            transitions.append({'arduino_ms': ms, 'id_hex': f'{ident:03X}', 'dlc': int(dlc), 'data_hex': payload})
            previous[ident] = payload
        if ident == 0x350 and data:
            pressed = bool(data[0] & 0x20)
            if pressed and pulse_start is None:
                pulse_start = ms
            elif not pressed and pulse_start is not None:
                pulses.append({'start_ms': pulse_start, 'end_ms': ms, 'duration_ms': ms - pulse_start})
                pulse_start = None
        if ident == 0x227 and len(data) >= 2:
            state = (data[1] >> 2) & 3
            if last_led is not None and state != last_led:
                led_changes.append({'arduino_ms': ms, 'before': last_led, 'after': state})
            last_led = state
    return {
        'source_file': path.name, 'source_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
        'valid_standard_data_frames': total, 'allowed_frames': selected,
        'transitions': transitions, 'defog_pulses': pulses, 'indicator_changes': led_changes,
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('logs', type=Path)
    args = parser.parse_args()
    result = {
        'schema': 'c4-climate-evidence-v1',
        'limits': ['Tempo Arduino; nao igualar as marcas de tempo do PC.',
                   'Captura de rede compartilhada nao atribui emissor fisico.',
                   'Somente transicoes de IDs 350, 364 e 227; nao e captura completa.',
                   'A captura com marcas incorretas (botao do motorista) foi descartada desta selecao.',
                   'Correlacao temporal nao comprova aquecimento eletrico no vidro.'],
        'captures': {label: extract(args.logs / file) for label, file in FILES.items()},
    }
    output = Path(__file__).resolve().parent.parent / 'research/clima-can/evidencias-2026-11-01.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'OK: {len(result["captures"])} capturas; apenas IDs 350/364/227 e hashes de origem.')
