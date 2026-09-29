"""Exercise section handoff, reader reuse and final cancellation ownership."""
import json
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch
import cycle_route
import route_session


class Client:
    def __init__(self,pid):
        self.pid=pid;self.shops=None;self.owned=False;self.execution_clearance=24
        self.installs=0;self.closes=0;self.navigation_rules={};self.cancel=False
    def cancelled(self):return self.cancel
    def wait_navigation_capsule(self):pass
    def install(self):self.owned=True;self.installs+=1
    def position(self):return (0,0,0)
    def __enter__(self):return self
    def __exit__(self,*args):self.closes+=1;self.owned=False


class Nav:
    def __init__(self,*args,**kwargs):pass
    def clear(self,*args):return True
    def fork(self):return Nav()


class Shops:
    def __init__(self,client,prefix,*args):
        self.client=client;self.path=prefix.with_suffix('.shops.jsonl')
        self.lock=threading.Lock();self.active=threading.Event();self.starts=0
        self.stats={'captured_shops':0};self.captured_keys=set();self.unavailable_keys=set()
        self.ignored_outside_keys=set();self.dynamic_targets={};self.pending={42:'pending reply'}
        self.pending_pause=False;self.pauses=0
    def prepare(self):pass
    def start(self):self.starts+=1;self.client.shops=self
    def summary(self):return self.stats
    def wait_after_capture(self,client,log,progress=None):
        if self.pending_pause:
            self.pending_pause=False;self.pauses+=1
        return 'ready',0


class SessionTests(unittest.TestCase):
    def test_two_sections_keep_one_owner_reader_pending_reply_and_navigation(self):
        client=Client(777);executed=[];readers=[]
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);command=root/'commands.json';output=root/'session.json'
            commands=[]
            for name in ('First','Second'):
                inp=root/(name+'.input.json');out=root/(name+'.json')
                inp.write_text(json.dumps({'city':'Giran','mode':'prices','shopPrefix':str(root/'shared.json'),
                    'localSection':True,'targets':[{'name':name,'x':0,'y':0,'object_id':1,'kiosk_type':1}]}))
                commands.append({'id':name,'input':str(inp),'output':str(out)})
            commands.append({'id':'end','finish':True})
            route={'points':[(0,0),(20,0)],'anchors':[], 'blockers':[], 'deferred':[],'destination':(20,0)}
            def follow(owner,*args):
                executed.append(set(owner.shops.local_section_keys));readers.append(owner.shops)
                if len(executed)==2:self.assertEqual(owner.shops.pauses,1)
                else:owner.shops.pending_pause=True
                self.assertTrue(owner.section_handoff)
                self.assertEqual(owner.shops.pending,{42:'pending reply'})
                self.assertEqual(owner.closes,0)
                # A live reader may have just begun the next UTF8 event.
                # Its incomplete line must not fail section acknowledgement.
                owner.shops.path.write_bytes(b'{"type":"incoming_player"}\n{"type":"shop","name":"\xd0')
                return {'reason':'completed','position':owner.position()}
            with patch.object(route_session,'WalkClient',return_value=client), \
                 patch.object(route_session,'next_command',side_effect=commands), \
                 patch.object(cycle_route,'load_navigation',return_value={}), \
                 patch.object(cycle_route,'price_navigation_data',return_value={}), \
                 patch.object(cycle_route,'Navigation',Nav), \
                 patch.object(cycle_route,'WalkGuard'), \
                 patch.object(cycle_route,'price_route',return_value=route), \
                 patch.object(cycle_route,'WalkShops',Shops), \
                 patch.object(cycle_route,'publish'), \
                 patch.object(cycle_route,'follow_with_recovery',side_effect=follow), \
                 patch.object(cycle_route,'revisit_missed',return_value=None):
                route_session.run(777,command,output)
            self.assertEqual(executed,[{'first'},{'second'}])
            self.assertEqual(client.shops.pauses,1)
            self.assertIs(readers[0],readers[1])
            self.assertEqual((client.installs,client.closes,client.shops.starts),(1,1,1))
            self.assertTrue(json.loads(output.read_text())['cleanupComplete'])
            self.assertEqual(json.loads(output.read_text())['sections'],2)
            self.assertTrue(json.loads((root/'Second.json').read_text())['shopStats'])

    def test_cancel_between_sections_closes_owner_before_final_output(self):
        client=Client(777)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);inp=root/'input.json';out=root/'section.json';final=root/'final.json'
            inp.write_text('{"mode":"prices"}')
            def section(owner,*args):owner.cancel=True;return {'reason':'cancelled'}
            with patch.object(route_session,'WalkClient',return_value=client), \
                 patch.object(route_session,'next_command',return_value={'id':'a','input':str(inp),'output':str(out)}), \
                 patch.object(route_session,'run_section',side_effect=section):
                route_session.run(777,root/'cmd.json',final)
            self.assertEqual(client.closes,1)
            self.assertEqual(json.loads(final.read_text())['reason'],'cancelled')

    def test_fault_acknowledged_and_final_success_never_written(self):
        client=Client(777)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);inp=root/'input.json';out=root/'section.json';final=root/'final.json'
            inp.write_text('{"mode":"prices"}')
            with patch.object(route_session,'WalkClient',return_value=client), \
                 patch.object(route_session,'next_command',return_value={'id':'a','input':str(inp),'output':str(out)}), \
                 patch.object(route_session,'run_section',side_effect=RuntimeError('bridge busy')):
                with self.assertRaisesRegex(RuntimeError,'bridge busy'):
                    route_session.run(777,root/'cmd.json',final)
            self.assertEqual(client.closes,1);self.assertFalse(final.exists())
            self.assertEqual(json.loads(out.read_text())['reason'],'section_failed')

    def test_replayed_command_ignored_and_other_profile_path_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);cmd=root/'cmd.json'
            cmd.write_text('{"id":"one","finish":true}')
            self.assertIsNone(route_session.next_command(cmd,'one'))
            cmd.write_text(json.dumps({'id':'two','input':str(root.parent/'other.json'),'output':str(root/'out.json')}))
            with self.assertRaisesRegex(ValueError,'leaves profile'):
                route_session.next_command(cmd,'one')

    def test_windows_atomic_replace_sharing_violation_retries_same_command(self):
        with patch.object(Path,'read_text',side_effect=[PermissionError('Windows sharing violation'),'{"id":"end","finish":true}']):
            self.assertIsNone(route_session.next_command(Path('cmd.json'),'previous'))
            self.assertEqual(route_session.next_command(Path('cmd.json'),'previous')['id'],'end')


if __name__=='__main__':unittest.main()
