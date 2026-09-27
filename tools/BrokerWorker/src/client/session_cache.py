"""Reuse only live-validated, PID-bound connection state between cycle phases."""
import json
import os
from pathlib import Path
import struct
from lu4_memory_client import Lu4MemoryClient


def valid_session(client,pid,data):
    if data.get('pid')!=pid or data.get('image_base')!=client.process_base(pid): return False
    rows=data.get('connection_candidates',[])
    if not data.get('connection_unique') or len(rows)!=1: return False
    row=rows[0];sockets=row.get('socket_candidates',[])
    if len(sockets)!=1:return False
    raw=client.read(pid,row['connection'],0x68)
    if len(raw)!=0x68 or struct.unpack_from('<Q',raw)[0]!=row['vtable'] or raw[0x31]!=1:return False
    if struct.unpack_from('<Q',raw,0x10)[0]!=row['socket_wrapper']:return False
    if row['rolling_key_address']!=row['connection']+0x57:return False
    raw=client.read(pid,row['socket_wrapper']+sockets[0]['offset'],4)
    return len(raw)==4 and struct.unpack('<I',raw)[0]==sockets[0]['handle']


def valid_active64(client,pid,data):
    if data.get('pid')!=pid or not data.get('matching_slot_unique'):return False
    base,size=client.active64_info()
    if (base,size)!=(data.get('active64_base'),data.get('active64_size')):return False
    slots=data.get('matching_slots',[])
    if len(slots)!=1:return False
    offset=slots[0]['selected_state_module_offset']
    if not 0x240CC<=offset<size-0x108:return False
    raw=client.read_active64(offset-0x240CC+0x50,0x14)
    return len(raw)==0x14 and raw[0]==1 and struct.unpack_from('<I',raw,0x10)[0]==pid


def reuse(pid,path,kind):
    root=Path(os.environ['LOCALAPPDATA'])/'PriceCheckCollector'
    shared=root/'research/market-walk'/str(pid)/'shops'/path.name
    # Broker and route use different folders but the same live connection.
    # Reuse is bidirectional and still requires all live validation below.
    runtime=root/'collection/runtime-sessions'
    candidates=sorted(runtime.glob(f'{pid}-*/*/BrokerRuntime/_internal/diagnostics/{path.name}'),
                      key=lambda p:p.stat().st_mtime,reverse=True)[:8]
    for candidate in dict.fromkeys((path,shared,*candidates)):
        try:
            data=json.loads(candidate.read_text(encoding='utf-8'))
            with Lu4MemoryClient() as client:
                valid=(valid_session if kind=='session' else valid_active64)(client,pid,data)
            if not valid:continue
            if candidate!=path:
                path.parent.mkdir(parents=True,exist_ok=True)
                path.write_text(json.dumps(data),encoding='utf-8')
            return True
        except (OSError,ValueError,KeyError,TypeError,RuntimeError,struct.error):continue
    return False
