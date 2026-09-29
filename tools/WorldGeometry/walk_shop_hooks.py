"""Owned nearby-shop transport. No ProcessEvent invocations on the reader thread."""
import contextlib
import io
import json
import struct
import subprocess
import sys
import time

from inspect_world import WORKER
import lu4_target_controller as target
import process_event_shop_capture as capture
from lu4_memory_client import Lu4MemoryClient, PAGE_READWRITE
from session_cache import reuse
from worker_progress import publish
from inspect_world import Memory
from walk_hook_sites import approach_slot, APPROACH_PREFIX


class ShopHooks:
    def __init__(self, walk):
        self.walk = walk
        self.folder = walk.directory / 'shops'
        self.folder.mkdir(exist_ok=True)
        self.paths = target.STATE_PATH, target.DIAGNOSTICS
        self.sender_owned = False
        self.suppression = None
        self.state_file = self.folder / 'approach-state.json'

    def prepare(self):
        # Bootstrap only, never while moving. Reuse PID/base-bound caches.
        for name, script, base_field in (
            ('latest_session.json', 'resolve_lu4_session.py', True),
            ('latest_active64_state.json', 'resolve_active64_state.py', False),
        ):
            path = self.folder / name
            if reuse(self.walk.pid,path,'session' if base_field else 'active64'):
                continue
            publish('Resolving this client connection before movement; first setup may take a few minutes')
            print(f'Preparing {name} for PID {self.walk.pid} before movement.', flush=True)
            with subprocess.Popen([sys.executable, str(WORKER/'diagnostics'/script), str(self.walk.pid),
                                   '--json', str(path)], stdout=subprocess.DEVNULL,
                                  stderr=subprocess.DEVNULL) as process:
                deadline = time.monotonic()+240
                while process.poll() is None:
                    if self.walk.cancelled() or time.monotonic()>deadline:
                        process.kill();process.wait()
                        raise RuntimeError('sender preparation cancelled or timed out')
                    time.sleep(.1)
                if process.returncode:
                    raise RuntimeError(f'{script} failed; see diagnostic setup')

    def install(self):
        if self.state_file.exists():
            raise RuntimeError(f'Unrestored shop hook: {self.state_file}')
        target.STATE_PATH = self.folder / 'target-state.json'
        target.DIAGNOSTICS = self.folder
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                target.install(self.walk.pid)
            self.sender_owned = True
            self.install_approach_guard()
            with contextlib.redirect_stdout(io.StringIO()):
                capture.set_suppress_ui(True)
                capture.set_suppress_target_ui(True)
        except BaseException:
            self.close()
            raise

    def install_approach_guard(self):
        pid = self.walk.pid
        with Lu4MemoryClient() as reader:
            slot, handler = approach_slot(Memory(reader, pid), self.walk.base)
            original = reader.read(pid, slot, 8)
            if struct.unpack('<Q', original)[0] != handler or reader.read(pid, handler, len(APPROACH_PREFIX)) != APPROACH_PREFIX:
                raise RuntimeError('MoveToObject handler changed; refusing shop reads')
            cave, _ = reader.allocate_process_memory(pid, 0x1000)
            # MoveToObject carries the moving actor ID at message+0x10. Forward
            # other actors unchanged: their approaches must remain visible.
            player_id = struct.unpack('<i',reader.read(pid,self.walk.world['player_actor']+0x550,4))[0]
            if player_id<=0:
                raise RuntimeError('invalid player ID for approach guard')
            code = capture.Code()
            code.emit(bytes.fromhex('9c 50 81 7a 10')+struct.pack('<i',player_id))
            code.branch32(bytes.fromhex('0f 85'),'forward')
            code.emit(b'\x48\xb8'+struct.pack('<Q',cave+0x800)+bytes.fromhex('f0 48 ff 00 58 9d c3'))
            code.label('forward')
            code.emit(bytes.fromhex('58 9d')+target.absolute_jump(handler,14))
            stub = code.finish()
            target.write_exact(reader, pid, cave, stub)
            target.write_exact(reader, pid, cave+0x800, bytes(8))
            target.flush_instruction_cache(pid, cave, len(stub))
            state = {'pid':pid, 'base':self.walk.base, 'slot':slot, 'original':original.hex(),
                     'cave':cave, 'player_id':player_id}
            self.state_file.write_text(json.dumps(state, indent=2), encoding='utf-8')
            self.suppression = state  # rollback metadata before the pointer mutation
            address, size, protection = reader.protect_process_memory(pid, slot, 8, PAGE_READWRITE)
            try:
                target.write_exact(reader, pid, slot, struct.pack('<Q', cave))
            finally:
                reader.protect_process_memory(pid, address, size, protection)

    def send(self, object_id, position):
        result = target.select_target(object_id, *position, False, fast=True, emit_output=False)
        if result['send_result'] != result['wire_length']:
            raise RuntimeError(f"short target send: {result['send_result']}/{result['wire_length']}")
        return result

    def suppressed_count(self):
        if not self.suppression:
            return 0
        with Lu4MemoryClient() as reader:
            return struct.unpack('<Q', reader.read(self.walk.pid, self.suppression['cave']+0x800, 8))[0]

    def cancel_target(self):
        result=target.select_target(0,0,0,0,False,fast=True,payload_override=b'\x48',
                                    packet_name='target_cancel',emit_output=False)
        if result['send_result']!=result['wire_length']:
            raise RuntimeError('target cancellation send failed')
        return result

    def close(self):
        errors = []
        try:
            if self.suppression:
                s = self.suppression
                with Lu4MemoryClient() as reader:
                    current = reader.read(s['pid'], s['slot'], 8)
                    if current not in (struct.pack('<Q', s['cave']), bytes.fromhex(s['original'])):
                        raise RuntimeError('approach slot ownership lost')
                    address, size, protection = reader.protect_process_memory(s['pid'], s['slot'], 8, PAGE_READWRITE)
                    try:
                        target.write_exact(reader, s['pid'], s['slot'], bytes.fromhex(s['original']))
                    finally:
                        reader.protect_process_memory(s['pid'], address, size, protection)
                    if reader.read(s['pid'], s['slot'], 8) != bytes.fromhex(s['original']):
                        raise RuntimeError('approach restore verification failed')
                # An in-flight handler may still be returning. Retain its 4KB until exit.
                self.state_file.replace(self.folder/'approach-restored.json')
                self.suppression = None
        except Exception as error:
            errors.append(str(error))
        try:
            if self.sender_owned:
                state = target.load_state()
                with Lu4MemoryClient() as reader:
                    for name in ('direct', 'post'):
                        patch = bytes.fromhex(state[name+'_patch_hex'])
                        if reader.read(self.walk.pid, state[name], len(patch)) != patch:
                            raise RuntimeError('sender hook ownership lost')
                    target.write_exact(reader,self.walk.pid,state['cave']+0x74,bytes(4))
                    for name in ('direct','post'):
                        original = bytes.fromhex(state[name+'_original_hex'])
                        target.patch_region(reader,self.walk.pid,state[name],original)
                        if reader.read(self.walk.pid,state[name],len(original)) != original:
                            raise RuntimeError('sender restore verification failed')
                # The producer has joined; a native return can still be in its cave.
                # Restore entry points, retain the 4KB until the game exits.
                target.STATE_PATH.replace(self.folder/'target-restored.json')
                self.sender_owned = False
        except Exception as error:
            errors.append(str(error))
        finally:
            target.STATE_PATH, target.DIAGNOSTICS = self.paths
        if errors:
            raise RuntimeError('; '.join(errors))
