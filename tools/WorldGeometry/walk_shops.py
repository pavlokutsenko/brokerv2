"""Asynchronous nearby-shop reader; movement never waits for a shop reply."""
from datetime import datetime, timezone
import json
import math
import os
import struct
import threading
import time
from pathlib import Path

from inspect_world import Memory, Lu4MemoryClient, GWORLD, GOBJECTS, read_object
from passby_event_collector import current_traders
from fast_headless_shop_sweep import read_history
import process_event_shop_capture as capture
from walk_shop_hooks import ShopHooks
from walk_shop_wire import ShopWire
from walk_shop_policy import eligible, generation, shop_rows, trader_key, server_nearby, choose_candidate, RADAR_DISCOVERY_RADIUS
from collection_zone import in_collection_zone
from walk_shop_lifecycle import close_visit,native_closed,native_closed_observations
from walk_session_bindings import continuation_nominations,needs_unbound_scan,bind_native_session_targets,session_candidate_matches
from walk_radar_targets import apply_radar_target


class WalkShops:
    def __init__(self, walk, prefix, radius=85, radar_file=None, broker_keys=None,collection_zone=None):
        if not 0<radius<=95:
            raise ValueError('shop radius must be >0 and <=95')
        self.walk, self.radius = walk, radius
        self.path = prefix.with_suffix('.shops.jsonl')
        self.hooks, self.wire = ShopHooks(walk), ShopWire(walk)
        self.active, self.halt = threading.Event(), threading.Event()
        self.discovery_ready=threading.Event()
        self.thread = None
        self.error = None
        self.pending, self.done, self.retry = {}, set(), {}
        self.allowed = {walk.initial_selected:float('inf')}
        self.last_selected = walk.initial_selected
        self.stats = {'captured_shops':0, 'exact_shops':0, 'unverified_shops':0, 'invalid_replies':0,
                      'rows':0, 'requests':0, 'target_cancels':0, 'timeouts':0, 'range_skips':0}
        self.closed = False
        self.server_position = None
        self.server_at = 0
        self.next_pair = 0
        self.lock=threading.Lock()
        self.candidate=None
        self.await_cancel=0
        self.allowed_keys=None
        self.allowed_object_ids=None
        self.tracked_targets=()
        self.priority_keys=set()
        self.trace_counts={}
        self.trace_last={}
        self.trace_actor_present=set()
        self.requested_keys=set()
        self.captured_keys=set()
        self.live_targets={}
        self.absence_scans={}
        self.last_actor_scan_at=0
        self.unavailable_keys=set()
        self.closed_stamps={}
        self.reopened_keys=set()
        self.unbound_absence_scans={};self.unbound_scan_at=0
        self.radar_file=Path(radar_file) if radar_file else None
        self.broker_keys=set(broker_keys or ())
        self.dynamic_targets={}
        self.collection_zone=collection_zone
        self.ignored_outside_keys=set()
        self.dynamic_seen_live={}
        self.dynamic_detour_attempted=set()
        self.dynamic_detours_enabled=True
        self.detour_target=None
        self.read_hold_key=None
        # The local pawn is CharacterMain_C; remote traders are CharacterPlayer_C.
        self.trader_class=read_object(walk.m,walk.base+GOBJECTS,218705)
        if walk.s.describe(self.trader_class)!={'address':hex(self.trader_class),
                'name':'CharacterPlayer_C','class':'BlueprintGeneratedClass'}:
            raise RuntimeError('trader class guard failed')

    def prepare(self):
        self.hooks.prepare()

    def start(self):
        try:
            self.hooks.install()
            self.wire.install()
            self.state = capture.load_state()
            with Lu4MemoryClient() as reader:
                self.sequence = int(capture.read_capture(reader,self.state)['sequence'])
            self.thread = threading.Thread(target=self.run, name='nearby-shop-reader', daemon=True)
            self.thread.start()
            self.walk.shops = self
            self.walk.cleanup_callbacks.append(self.close)
        except BaseException:
            self.close()
            raise

    def allowed_target(self, pointer):
        if pointer == self.last_selected:
            return True
        if time.monotonic() <= self.allowed.get(pointer,0):
            self.last_selected = pointer
            return True
        return False

    def position(self, memory):
        memory.pages.clear()
        w = self.walk.world
        if memory.u64(self.walk.base+GWORLD)!=w['world'] or memory.u64(w['controller']+0x2D0)!=w['player_actor']:
            raise RuntimeError('shop reader world/pawn changed')
        return struct.unpack('<3d',memory.read(w['player_capsule']+0x1F0,24))

    def fresh(self, memory, trader):
        try:
            return self.read_trader(memory,trader)
        except (OSError,struct.error):
            return None  # actor streamed out; skip this trader, keep the route

    def read_trader(self, memory, trader):
        memory.pages.clear()
        actor = int(trader['actor'],16)
        if memory.u64(actor+0x10)!=self.trader_class:
            return None
        root = memory.u64(actor+0x1A0)
        if not root:
            return None
        return {'actor':trader['actor'], 'object_id':memory.i32(actor+0x550),
                'name':memory.fstring(actor,0x558,32), 'kiosk_type':memory.i32(actor+0x7DC),
                **dict(zip(('x','y','z'),struct.unpack('<3d',memory.read(root+0x1F0,24))))}

    def send_pair(self, memory, trader, log):
        oid = trader['object_id']
        key = trader_key(trader['name'])
        target = self.dynamic_targets.get(key) or next((t for t in getattr(self,'tracked_targets',()) if t['key']==key), {})
        self.retry[generation(trader)] = time.monotonic()+30
        attempt = {'trader':trader, 'sent':time.monotonic(), 'actions':[], 'capture':None, 'wire':None,
                   'verification_revision':target.get('verification_revision'),
                   'read_started_at':datetime.now(timezone.utc).isoformat(),
                   'reopened':bool(self.dynamic_targets.get(trader_key(trader['name']),{}).get('reopened'))}
        self.pending[oid] = attempt
        for action in (1,2):
            if not self.active.is_set() or self.halt.is_set() or self.walk.cancelled():
                return
            current = self.fresh(memory,trader)
            position = self.position(memory)
            # Reserve one movement frame for the second action. Both actions
            # still validate the same live actor and the full read radius.
            request_radius = max(0,self.radius-8) if action==1 else self.radius
            if (not eligible(trader,current,position,request_radius)
                or not in_collection_zone(self.collection_zone,current['x'],current['y'])
                or not server_nearby(current,self.server_position,time.monotonic()-self.server_at,
                                     self.radius,position,
                                     stationary_probe=trader_key(trader['name'])==getattr(self,'read_hold_key',None))):
                self.stats['range_skips'] += 1
                log({'type':'range_skip','object_id':oid,'action':action,
                     'trader_key':trader_key(trader['name']),'actor_valid':current is not None,
                     'local_eligible':eligible(trader,current,position,request_radius),
                     'request_radius':request_radius,
                     'server_age':time.monotonic()-self.server_at,
                     'distance':math.dist(position[:2],(current['x'],current['y'])) if current else None,
                     'server_distance':math.dist(self.server_position[:2],(current['x'],current['y']))
                         if current and self.server_position else None})
                if action==1:
                    del self.pending[oid]
                    self.retry[generation(trader)]=time.monotonic()+.2
                return
            self.allowed[int(trader['actor'],16)] = time.monotonic()+5
            started=time.monotonic()
            response = self.hooks.send(oid,position)
            self.stats['requests'] += 1
            self.requested_keys.add(trader_key(trader['name']))
            detail = {'action':action, 'position':position,
                      'stationary_probe':trader_key(trader['name'])==getattr(self,'read_hold_key',None),
                      'server_age':round(time.monotonic()-self.server_at,3),
                      'send_ms':round((time.monotonic()-started)*1000,3),
                      'server_position':self.server_position,
                      'server_distance':math.dist(self.server_position[:2],(current['x'],current['y'])),
                      'distance':math.dist(position[:2],(current['x'],current['y'])), **response}
            attempt['actions'].append(detail)
            log({'type':'request','trader_key':trader_key(trader['name']), **detail})

    def before_move(self):
        # One owner sends actions immediately BEFORE the next ordinary movement
        # command. Otherwise an action stops server movement until the next tick.
        # Replies and discovery remain on the background reader.
        if not self.active.is_set() or time.monotonic()<self.next_pair or not self.lock.acquire(blocking=False):
            return
        try:
            trader=self.candidate
            self.candidate=None
            if trader is None or trader['object_id'] in self.pending or generation(trader) in self.done:
                return
            if trader_key(trader['name']) in self.unavailable_keys: return
            before=self.stats['requests']
            try:
                with Lu4MemoryClient() as reader:
                    self.send_pair(Memory(reader,self.walk.pid),trader,self.log)
            finally:
                if self.stats['requests']>before:
                    self.allowed[0]=time.monotonic()+5
                    with Lu4MemoryClient() as reader:
                        self.await_cancel=self.wire.cancel_replies(reader)+1
                    response=self.hooks.cancel_target()
                    self.stats['target_cancels']+=1
                    self.log({'type':'target_cancel',**response})
            # An 85-unit pass often leaves under a second in range. Space
            # requests enough to let movement resume, but do not skip the
            # next nearby shop solely because the previous pair was sent.
            self.next_pair=time.monotonic()+.3
        finally:
            self.lock.release()

    def needs_resume(self):
        if self.await_cancel and self.wire.cancel_replies(self.walk.client)>=self.await_cancel:
            self.await_cancel=0
            return True
        return False

    def consume(self, reader, log):
        for wire in self.wire.read(reader):
            pending = self.pending.get(wire['object_id'])
            if pending is not None:
                pending['wire'] = wire
        for row in self.wire.observed:
            if row['hex'].startswith(('2f','72','24')):
                log({'type':'incoming_player','sequence':row['sequence'],'length':row['length'],'hex':row['hex']})
            raw=bytes.fromhex(row['hex'])
            # Observed moving-actor replies: origin xyz, NOT destination xyz.
            if (raw[0],len(raw)) in ((0x2f,29),(0x72,37)):
                self.server_position=struct.unpack_from('<3i',raw,5 if raw[0]==0x2f else 13)
                self.server_at=time.monotonic()
        for event in read_history(reader,self.state):
            seq = int(event['sequence'])
            if seq<=self.sequence:
                continue
            if seq-self.sequence>8:
                raise RuntimeError('shop event capture overrun')
            self.sequence = seq
            pending = self.pending.get(event['object_id'])
            if pending is not None:
                pending['capture'] = event
        now = time.monotonic()
        for oid, pending in list(self.pending.items()):
            event,wire = pending['capture'],pending['wire']
            # A1 copy precedes the event; a concurrent read may see the event first.
            if event is not None and (wire is not None or now-pending['sent']>.25):
                try:
                    rows, precision = shop_rows(event,wire,pending['trader'])
                except ValueError as error:
                    self.stats['invalid_replies']+=1
                    log({'type':'invalid_reply','trader':pending['trader'],'error':str(error)})
                    del self.pending[oid]
                    continue
                if event.get('function_name')=='PlayerShopSellItemsList' and wire is None:
                    if now-pending['sent']<2:
                        continue
                    raise RuntimeError('normal sell response has no exact wire capture')
                log({'type':'shop','trader_key':trader_key(pending['trader']['name']),
                     'trader':pending['trader'],'side':event['side'],'precision':precision,
                     'row_count':len(rows),'rows':rows,'capture_sequence':event['sequence'],
                     'verification_revision':pending.get('verification_revision'),
                     'read_started_at':pending.get('read_started_at'),
                     'reopened':pending.get('reopened',False),
                     'event_function':event['function_name'],'event_parser_caller':event.get('parser_caller'),
                     'event_buy_converter_caller':event.get('alternate_converter_caller'),
                     'actions':pending['actions']})
                self.done.add(generation(pending['trader']))
                if precision=='wire_int64':
                    self.captured_keys.add(trader_key(pending['trader']['name']))
                self.stats['captured_shops']+=1
                self.stats['rows']+=len(rows)
                self.stats['exact_shops' if precision=='wire_int64' else 'unverified_shops']+=1
                del self.pending[oid]
            elif now-pending['sent']>2:
                self.stats['timeouts']+=1
                log({'type':'timeout','trader':pending['trader'],'actions':len(pending['actions'])})
                del self.pending[oid]

    def refresh_radar(self, log):
        if self.radar_file is None or not self.radar_file.exists():
            return
        try:
            payload=json.loads(self.radar_file.read_text(encoding='utf-8'))
            observed=datetime.fromisoformat(payload['at'])
            if abs((datetime.now(timezone.utc)-observed).total_seconds())>2:
                return
            if payload.get('pid') is not None and payload['pid']!=self.walk.pid:
                return
            if payload.get('pid') is not None:
                continuation_nominations(self,payload,log)
            if payload.get('pid') is not None and payload['pid']==self.walk.pid:
                bindings={trader_key(row['name']):row for row in payload.get('bindings',[])}
                for target in self.tracked_targets:
                    if not target.get('rebind') or target['object_id']!=0:
                        continue
                    row=bindings.get(target['key'])
                    if (row is None or int(row['object_id'])<=0
                            or int(row['kiosk_type'])!=target['kiosk_type']
                            or math.dist((row['x'],row['y']),(target['x'],target['y']))>=20):
                        continue
                    target['object_id']=int(row['object_id'])
                    self.allowed_object_ids.add(target['object_id'])
                    log({'type':'runtime_rebound','key':target['key'],'object_id':target['object_id']})
            for row in payload.get('closed',[]):
                if row.get('kiosk_type')==0:
                    close_visit(self,trader_key(row['name']),int(row['object_id']),log,'character packet kiosk=0',row.get('observed_at'))
            for row in payload['traders']:
                apply_radar_target(self,row,log)
        except (OSError,ValueError,KeyError,TypeError):
            return  # A replaced frame can be retried without stopping movement.

    def claim_dynamic_detour(self, position, path, arc):
        if not self.dynamic_detours_enabled:
            return None
        candidates=sorted((t for t in list(self.dynamic_seen_live.values())
            if t['key'] not in self.requested_keys and t['key'] not in self.dynamic_detour_attempted
            and (not getattr(self,'local_section_mode',False) or t['key'] in self.local_section_keys)
            and time.monotonic()-t['seen_at']<=2
            and in_collection_zone(self.collection_zone,t['x'],t['y'])
            and self.radius<math.dist(position[:2],(t['x'],t['y']))<=RADAR_DISCOVERY_RADIUS),
            key=lambda t:math.dist(position[:2],(t['x'],t['y'])))
        for target in candidates:
            # Keep moving when the existing coverage route passes this shop
            # soon. Only a shop outside that corridor needs an extra approach.
            if min(math.dist(path.at(arc+ahead),(target['x'],target['y']))
                   for ahead in range(50,501,50))<=90:
                continue
            self.dynamic_detour_attempted.add(target['key'])
            self.detour_target=target
            return target
        return None

    def run(self):
        snapshot = {'pid':self.walk.pid, **{k:hex(self.walk.world[k]) for k in ('persistent_level','player_actor')}}
        try:
            with Lu4MemoryClient() as reader, self.path.open('w',encoding='utf-8',buffering=1) as output:
                def log(value):
                    output.write(json.dumps({'at':datetime.now(timezone.utc).isoformat(),**value},ensure_ascii=False)+'\n')
                    if value.get('type')=='shop':
                        output.flush()
                        os.fsync(output.fileno())
                self.log=log
                memory = Memory(reader,self.walk.pid)
                traders=[];last_scan=0;last_radar=0
                velocity=(0,0);velocity_position=None;velocity_at=0
                while not self.halt.is_set():
                    with self.lock:
                        self.consume(reader,log)
                    now = time.monotonic()
                    # Radar frames are safe file reads and remain current while
                    # movement pauses for a replan. Shop requests stay gated.
                    if not self.walk.cancelled() and now-last_radar>.5:
                        self.refresh_radar(log)
                        last_radar=now
                    if self.active.is_set() and not self.walk.cancelled():
                        scanned=False
                        if now-last_scan>.25:
                            scan_position=self.position(memory)
                            allowed=None if needs_unbound_scan(self,scan_position) else self.allowed_object_ids
                            actors,scan_ms=current_traders(reader,snapshot,allowed,include_closed=True)
                            bind_native_session_targets(self,memory,actors,scan_position,log)
                            native_closed_observations(self,memory,actors,log)
                            traders=[t for t in actors if t['kiosk_type'] in (1,3,8)]
                            last_scan=time.monotonic()
                            self.stats['last_scan_ms']=round(scan_ms,2)
                            scanned=True
                        position=self.position(memory)
                        if velocity_position is None or now-velocity_at>=.2:
                            if velocity_position is not None:
                                gap=max(.01,now-velocity_at)
                                velocity=tuple((position[k]-velocity_position[k])/gap for k in (0,1))
                                speed=math.hypot(*velocity)
                                if speed>200: velocity=tuple(v*200/speed for v in velocity)
                            velocity_position=position;velocity_at=now
                        if scanned:
                            previous=self.live_targets
                            self.live_targets={trader_key(t['name']):{**t,'key':trader_key(t['name']),
                                               'seen_at':time.monotonic()} for t in traders}
                            native_closed(self,memory,previous,self.live_targets,log)
                            self.last_actor_scan_at=time.monotonic()
                            for key in tuple(self.allowed_keys or ()):
                                self.absence_scans[key]=0 if key in self.live_targets else self.absence_scans.get(key,0)+1
                            self.ignored_outside_keys.update(trader_key(t['name']) for t in traders
                                if not in_collection_zone(self.collection_zone,t['x'],t['y']))
                            for trader in traders:
                                key=trader_key(trader['name'])
                                target=self.dynamic_targets.get(key)
                                if (target is not None and target['object_id']==trader['object_id']
                                        and math.dist(position[:2],(trader['x'],trader['y']))<=RADAR_DISCOVERY_RADIUS):
                                    previous=self.dynamic_seen_live.get(key)
                                    if previous is None:
                                        log({'type':'radar_live_admitted','key':key,
                                             'object_id':trader['object_id'],'position':position,
                                             'distance':math.dist(position[:2],(trader['x'],trader['y']))})
                                    self.dynamic_seen_live[key]={**target,'x':trader['x'],'y':trader['y'],
                                                                 'seen_at':now,
                                                                 'admitted_at':previous.get('admitted_at',now) if previous else now}
                            self.discovery_ready.set()
                            for target in self.tracked_targets:
                                key=target['key']
                                distance=math.dist(position[:2],(target['x'],target['y']))
                                if distance>self.radius+5 or self.trace_counts.get(key,0)>=3 \
                                        or now-self.trace_last.get(key,0)<.3:
                                    continue
                                found=next((t for t in traders if trader_key(t['name'])==key
                                    and t['object_id']==target['object_id']),None)
                                if found is not None:
                                    self.trace_actor_present.add(key)
                                queued=self.candidate
                                self.trace_counts[key]=self.trace_counts.get(key,0)+1
                                self.trace_last[key]=now
                                log({'type':'target_window','key':key,'distance':round(distance,1),
                                     'actor_present':found is not None,
                                     'actor_count':len(traders),'scan_ms':round(scan_ms,2),
                                     'server_age':round(now-self.server_at,3),
                                     'server_distance':round(math.dist(self.server_position[:2],
                                         (target['x'],target['y'])),1) if self.server_position else None,
                                     'next_pair_in':round(max(0,self.next_pair-now),3),
                                     'candidate':trader_key(queued['name']) if queued else None,
                                     'pending':target['object_id'] in self.pending})
                        candidates = [t for t in traders if generation(t) not in self.done
                                      and trader_key(t['name']) not in self.unavailable_keys
                                      and in_collection_zone(self.collection_zone,t['x'],t['y'])
                                      and (self.allowed_keys is None or trader_key(t['name']) in self.allowed_keys)
                                      and session_candidate_matches(self,t)
                                      and t['object_id'] not in self.pending
                                      and now>=self.retry.get(generation(t),0)
                                      and math.dist(position[:2],(t['x'],t['y']))<=self.radius
                                      and server_nearby(t,self.server_position,now-self.server_at,self.radius,position,
                                          stationary_probe=trader_key(t['name'])==self.read_hold_key)]
                        if candidates and len(self.pending)<4 and now>=self.next_pair:
                            trader=choose_candidate(candidates,position,velocity,self.radius,self.priority_keys)
                            with self.lock:
                                self.candidate=trader
                    self.halt.wait(.02)
                # Keep approach suppression until all outstanding replies have drained.
                deadline=time.monotonic()+2.1
                while self.pending and time.monotonic()<deadline:
                    self.consume(reader,log)
                    time.sleep(.02)
        except Exception as error:
            self.error = str(error)
            self.active.clear()

    def close(self):
        if self.closed:
            return
        self.active.clear();self.halt.set()
        if self.thread:
            self.thread.join(7)
            if self.thread.is_alive():
                raise RuntimeError('shop sender still active; hooks retained for recovery')
        self.stats['suppressed_approaches']=self.hooks.suppressed_count()
        if self.wire.hook:
            with Lu4MemoryClient() as reader:
                self.stats['target_cancel_replies']=self.wire.cancel_replies(reader)
        errors=[]
        for close in (self.wire.close,self.hooks.close):
            try: close()
            except Exception as error: errors.append(str(error))
        self.closed=True
        if errors:
            raise RuntimeError('; '.join(errors))

    def summary(self):
        return {**self.stats,'radius':self.radius,'log':str(self.path),'error':self.error}
