"""One native ownership lane and reader for an entire finite price pass."""
import json
import os
import time
from pathlib import Path
from walk_client import WalkClient
from cycle_route import run_section, write


def next_command(path, previous):
    try:
        command = json.loads(path.read_text(encoding='utf-8-sig'))
        if command.get('id') == previous:
            return None
        if not command.get('id'):
            raise ValueError('Route session command has no identity')
        if not command.get('finish'):
            for field in ('input', 'output'):
                if Path(command[field]).resolve().parent != path.resolve().parent:
                    raise ValueError('Route session command leaves profile runs directory')
        return command
    except (OSError, json.JSONDecodeError):
        return None


def run(pid, command_file, output):
    previous = None; sections = 0; reason = 'completed'; waiting=time.monotonic();scope=None
    # Cancellation, final rest and hook cleanup belong to the session boundary.
    # Section outputs acknowledge execution; the host awaits this final output
    # before changing native role, returning to broker or changing the client.
    with WalkClient(pid) as client:
        while not client.cancelled():
            command = next_command(command_file, previous)
            if command is None:
                if time.monotonic()-waiting>10:
                    raise TimeoutError('Route session host did not submit its next section within10 seconds')
                time.sleep(.02)
                continue
            previous = command['id']
            if command.get('finish'):
                break
            section_output = Path(command['output'])
            config = json.loads(Path(command['input']).read_text(encoding='utf-8-sig'))
            if config['mode'] != 'prices':
                raise ValueError('Continuous session accepts price sections only')
            current_scope=(config.get('profileId'),config.get('market'),config.get('city'))
            if scope is not None and scope!=current_scope:
                raise ValueError('Route session profile/market/city changed')
            scope=current_scope
            config['continuousSession'] = True
            os.environ['PRICECHECK_PROGRESS_FILE']=str(section_output.with_suffix('.progress.json'))
            try:
                result = run_section(client, config, section_output)
            except Exception as error:
                # Preserve exact captures already persisted by the section.
                result=json.loads(section_output.read_text(encoding='utf-8')) if section_output.exists() else {
                    'shops': [], 'failures': []}
                write(section_output, {**result, 'reason': 'section_failed', 'error': str(error)})
                raise
            write(section_output, result)
            sections += 1
            waiting=time.monotonic()
            reason = result['reason']
            if reason in ('cancelled', 'keyboard_interrupt', 'target_changed_externally',
                          'position_jump', 'shop_error'):
                break
        if client.cancelled():
            reason = 'cancelled'
    write(output, {'reason': reason, 'sections': sections, 'cleanupComplete': True})
