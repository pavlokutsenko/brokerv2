"""Filtered, read-only A1 observation for exact sell-shop int64 prices/counts."""
import json
import struct
import sys

from inspect_world import ROOT
from pathlib import Path
remote=Path(__file__).resolve().parent.parent/'remote'
sys.path.insert(0, str(remote if remote.is_dir() else ROOT/'tools/RemotePrices'))
import packet_capture as packets
from lab import Code, absolute_jump, patch_region, write_exact, Lu4MemoryClient
from shop_wire import decode_sell_shop, decode_buy_shop


class ShopWire:
    def __init__(self, walk):
        self.walk = walk
        self.path = walk.directory/'shops'/'wire-state.json'
        self.hook = None
        self.after = 0
        self.observed = []

    def install(self):
        if self.path.exists():
            raise RuntimeError(f'Unrestored shop wire observer: {self.path}')
        pid = self.walk.pid
        with Lu4MemoryClient() as reader:
            address = packets.discover(reader, pid, self.walk.base)
            if reader.read(pid, address-17, len(packets.RX_SIGNATURE)) != packets.RX_SIGNATURE:
                raise RuntimeError('shop receive site changed')
            original = reader.read(pid, address, len(packets.RX_ORIGINAL))
            if original != packets.RX_ORIGINAL:
                # Preserve the existing radar observer, with its established signature.
                if original[:6] != bytes.fromhex('ff2500000000'):
                    raise RuntimeError('unrecognized receive observer')
                previous = struct.unpack_from('<Q',original,6)[0]
                raw = reader.read(pid,previous,0x81)
                ring = struct.unpack_from('<Q',raw,0x29)[0]
                if not ring or struct.unpack_from('<Q',raw,0x43)[0]!=ring+4 or struct.unpack_from('<Q',raw,0x79)[0]!=ring:
                    raise RuntimeError('receive observer is not the known radar hook')
            cave, _ = reader.allocate_process_memory(pid, 0x1000)
            records = []
            try:
                for _ in range(packets.CAPACITY):
                    record, _ = reader.allocate_process_memory(pid, packets.SLOT_SIZE)
                    records.append(record)
                control, table = cave+0x800, cave+0x900
                code = Code()
                code.emit(packets.PUSH)
                code.emit(bytes.fromhex('41 80 3e a1'))  # cmp byte [r14],A1
                code.branch32(bytes.fromhex('0f 84'), 'capture')
                code.emit(bytes.fromhex('41 80 3e be'))  # exact buy reply
                code.branch32(bytes.fromhex('0f 84'), 'capture')
                code.emit(bytes.fromhex('83 fe 05'))  # esi length >=5
                code.branch32(bytes.fromhex('0f 8c'), 'passthrough')
                player_id=struct.unpack('<i',reader.read(pid,self.walk.world['player_actor']+0x550,4))[0]
                code.emit(bytes.fromhex('41 81 7e 01')+struct.pack('<i',player_id))
                code.branch32(bytes.fromhex('0f 85'), 'passthrough')
                code.label('capture')
                code.emit(bytes.fromhex('41 80 3e 24'))
                code.branch32(bytes.fromhex('0f 85'),'copy')
                code.emit(b'\x48\xb8'+struct.pack('<Q',control+24)+bytes.fromhex('f0 ff 00'))
                code.label('copy')
                code.emit(packets.build_stub('rx',control,table,original,address+len(original))[len(packets.PUSH):])
                code.label('passthrough')
                code.emit(packets.POP+original+absolute_jump(address+len(original),14))
                stub = code.finish()
                patch = absolute_jump(cave,len(original))
                hook = {'kind':'rx', 'address':address, 'original':original.hex(), 'patch':patch.hex(),
                        'control':control, 'cave':cave, 'records':records, 'pid':pid, 'base':self.walk.base}
                write_exact(reader,pid,cave,stub)
                write_exact(reader,pid,control,bytes(32))
                write_exact(reader,pid,table,struct.pack(f'<{len(records)}Q',*records))
                self.path.write_text(json.dumps(hook,indent=2),encoding='utf-8')
                self.hook = hook
                patch_region(reader,pid,address,patch)
            except BaseException:
                if self.hook is None:
                    for record in records:
                        reader.free_process_memory(pid,record)
                    reader.free_process_memory(pid,cave)
                else:
                    self.close()
                raise

    def read(self, reader):
        rows,self.after,dropped = packets.snapshot(reader,self.walk.pid,self.hook,self.after)
        if dropped:
            raise RuntimeError('shop wire observer dropped a packet')
        self.observed=[]
        decoded=[]
        for row in rows:
            raw=bytes.fromhex(row['hex'])
            if raw[0]==0xa1:
                decoded.append(decode_sell_shop(raw))
            elif raw[0]==0xbe:
                try:
                    decoded.append(decode_buy_shop(raw))
                except ValueError:
                    self.observed.append(row)  # Unknown BE variant remains unverified.
            else:
                self.observed.append(row)
        return decoded

    def cancel_replies(self, reader):
        return struct.unpack('<I',reader.read(self.walk.pid,self.hook['control']+24,4))[0]

    def close(self):
        if self.hook is None:
            return
        h = self.hook
        with Lu4MemoryClient() as reader:
            if reader.process_base(h['pid']) != h['base']:
                raise RuntimeError('shop observer PID/base changed')
            original,patch = bytes.fromhex(h['original']),bytes.fromhex(h['patch'])
            if reader.read(h['pid'],h['address'],len(patch)) not in (patch,original):
                raise RuntimeError('shop observer ownership lost')
            patch_region(reader,h['pid'],h['address'],original)
            if reader.read(h['pid'],h['address'],len(original)) != original:
                raise RuntimeError('shop observer restore failed')
        # In-flight native frames can still reference the ring. Keep ~1MB until exit.
        self.path.replace(self.path.with_name('wire-restored.json'))
        self.hook = None
