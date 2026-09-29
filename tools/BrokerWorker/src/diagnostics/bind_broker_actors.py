"""Bind broker identities to guarded loaded actors; never admits radar-only shops."""
import json
import math
from pathlib import Path
import sys
import time
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'client'),str(ROOT/'diagnostics')]
NAVIGATION = ROOT/'navigation' if (ROOT/'navigation').is_dir() else ROOT.parents[2]/'tools/WorldGeometry'
sys.path.insert(0,str(NAVIGATION))
from lu4_memory_client import Lu4MemoryClient
from scan_lu4_actors import Memory,pe_info,validate_world_slot,coherent_actor_snapshot,pointer
from inspect_shop_ufunctions import read_object,decode_name
from worker_progress import check_stop,publish
from walk_runtime import resolve_runtime


def binding_rejection(oid, root, name, kind, x, y):
    if oid<=0: return 'invalid_object_id'
    if not root: return 'missing_root'
    if not name: return 'missing_name'
    # Identity survives a shop closing. Only broker-returned IDs are admitted
    # by merge_bindings; a standing actor never nominates a price request.
    # Live guarded actors Leprechaun/JasonTod use6. They remain identities,
    # while price admission still permits only broker-bound types1/3/8.
    if kind not in (0,1,2,3,4,5,6,8,9): return 'unsupported_kiosk'
    if not all(math.isfinite(v) and abs(v)<10_000_000 for v in (x,y)): return 'invalid_position'
    return None


class CaptureMemory(Memory):
    # Memory's diagnostic accessors return defaults on failed reads. Such a
    # default must never count as evidence that a player was absent.
    def __init__(self, client, pid):
        super().__init__(client, pid)
        self.read_errors = 0

    def read(self, address, size):
        try:
            return super().read(address, size)
        except (OSError, RuntimeError):
            self.read_errors += 1
            raise


def radar_complete(actors, final_actors, world, final_world, rejected, read_errors, wanted):
    return (wanted is None and read_errors == 0 and not rejected and actors == final_actors and
        final_world is not None and all(world[key] == final_world[key]
            for key in ('world', 'persistent_level', 'controller', 'player_actor')) and
        math.dist(world['player'][:2], final_world['player'][:2]) < 10)


def capture(pid, wanted=None):
    capture_started=datetime.now(timezone.utc).isoformat()
    bindings=[]
    rejected=[]
    observed=set()
    with Lu4MemoryClient() as client:
        base=client.process_base(pid);mem=CaptureMemory(client,pid);pe=pe_info(mem,base)
        rvas,indices,world,survey=resolve_runtime(mem,base,pe)
        if not world: raise RuntimeError('World/controller unavailable')
        cls=read_object(mem,base+rvas['gobjects'],indices['trader_class'])
        if survey.describe(cls)['class']!='BlueprintGeneratedClass' or survey.name(cls)!='CharacterPlayer_C':
            raise RuntimeError('Trader class guard changed')
        actors,_=coherent_actor_snapshot(mem,world['persistent_level'])
        for actor in actors:
            if not pointer(actor): continue
            if mem.u64(actor+0x10)!=cls: continue
            oid=mem.i32(actor+0x550)
            if wanted is not None and oid not in wanted: continue
            observed.add(oid)
            root=mem.u64(actor+0x1A0)
            name=mem.fstring(actor,0x558,32);kind=mem.i32(actor+0x7DC)
            x=mem.f64(root+0x1F0) if root else math.nan
            y=mem.f64(root+0x1F8) if root else math.nan
            reason=binding_rejection(oid,root,name,kind,x,y)
            if reason:
                rejected.append({'object_id':oid,'name':name,'kiosk_type':kind,'reason':reason})
                continue
            bindings.append({'object_id':oid,'name':name,'kiosk_type':kind,
                'x':x,'y':y})
        final_mem=CaptureMemory(client,pid)
        final_world=validate_world_slot(final_mem,base+rvas['gworld'],base,pe['image_size'])
        final_actors,_=coherent_actor_snapshot(final_mem,world['persistent_level'])
        complete=radar_complete(actors,final_actors,world,final_world,rejected,
            mem.read_errors+final_mem.read_errors,wanted)
    return {'pid':pid,'module_base':base,'at':datetime.now(timezone.utc).isoformat(),'bindings':bindings,
        'actor_count':len(actors),'player':world['player'],
        'capture_started_at':capture_started,'radar_complete':complete,
        'rejected':rejected,'missing_actor_ids':sorted(wanted-observed) if wanted is not None else []}


def merge_bindings(before, after, started_at, wanted, during=()):
    started=datetime.fromisoformat(started_at)
    age=(started-datetime.fromisoformat(before['at'])).total_seconds()
    if before['pid']!=after['pid'] or before['module_base']!=after['module_base'] or not 0<=age<=10:
        raise RuntimeError('Pre-broker identity snapshot does not belong to this pass')
    by_id={row['object_id']:row for row in before['bindings'] if row['object_id'] in wanted}
    previous=datetime.fromisoformat(before['at'])
    for sample in during:
        at=datetime.fromisoformat(sample['at'])
        if sample['pid']!=after['pid'] or sample['module_base']!=after['module_base'] or at<started or at<previous:
            raise RuntimeError('Intermediate identity snapshot does not belong to this pass')
        previous=at
        by_id.update({row['object_id']:row for row in sample['bindings'] if row['object_id'] in wanted})
    if after.get('at') and datetime.fromisoformat(after['at'])<previous:
        raise RuntimeError('Final identity snapshot predates intermediate snapshot')
    by_id.update({row['object_id']:row for row in after['bindings'] if row['object_id'] in wanted})
    return list(by_id.values())


def resolve_bindings(before, after, started_at, wanted, during=(), take=capture, wait=time.sleep):
    # Identity gaps warn and preserve the epoch as partial. No extra waits or
    # retries at the broker; the next natural cycle gets a fresh observation.
    return merge_bindings(before,after,started_at,wanted,during),[]


def final_radar_capture(pid, take=capture, wait=time.sleep):
    # Never merge partial captures into completeness. Retry a transient actor
    # array change, retaining only one independently complete final capture.
    for attempt in range(3):
        after=take(pid)
        if after.get('radar_complete',False): return after
        if attempt<2: wait(.25)
    return after


def bind(pid,output):
    value=json.loads(output.read_text(encoding='utf-8'))
    wanted={r['trader_object_id'] for r in value['rows']}
    after=final_radar_capture(pid)
    after['missing_actor_ids']=sorted(wanted-{row['object_id'] for row in after['bindings']})
    before_path=output.with_suffix('.actors-before.json')
    bindings=[row for row in after['bindings'] if row['object_id'] in wanted]
    attempts=[]
    if before_path.exists():
        before=json.loads(before_path.read_text(encoding='utf-8'))
        during_path=output.with_suffix('.actors-during.json')
        during=json.loads(during_path.read_text(encoding='utf-8')) if during_path.exists() else []
        bindings,attempts=resolve_bindings(before,after,value['started_at'],wanted,during)
        value['binding_before_at']=before['at']
    value['binding_after_count']=len(after['bindings'])
    value['binding_diagnostics']={key:after[key] for key in ('rejected','missing_actor_ids')}
    value['binding_diagnostics']['unresolved_ids']=sorted(wanted-{row['object_id'] for row in bindings})
    value['binding_diagnostics']['closed_identity_count']=sum(row['kiosk_type'] not in (1,3,8) for row in bindings)
    value['binding_diagnostics']['retries']=attempts
    names={row['object_id']:row['name'] for row in bindings}
    for row in value['rows']: row['trader_name']=names.get(row['trader_object_id'],'')
    value['summary']['named_traders']=len(wanted & names.keys())
    value['bindings']=bindings;value['binding_pid']=pid
    value['native_state_observations']=[{**row,'observed_at':after['at'],
        'collector_x':after['player'][0],'collector_y':after['player'][1]}
        for row in after['bindings']]
    value['native_radar']={'pid':pid,'complete':after.get('radar_complete',False),
        'started_at':after.get('capture_started_at'), 'observed_at':after['at'],
        'actor_count':after['actor_count'],'identity_count':len(after['bindings']),
        'collector_x':after['player'][0],'collector_y':after['player'][1]}
    temporary=output.with_suffix('.tmp');temporary.write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8')
    temporary.replace(output)

if __name__=='__main__': bind(int(sys.argv[1]),Path(sys.argv[2]))
