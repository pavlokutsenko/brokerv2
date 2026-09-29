import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'tools'/'BrokerWorker'/'src'
sys.path[:0]=[str(ROOT/'diagnostics'),str(ROOT/'client')]
from bind_broker_actors import radar_complete, CaptureMemory, binding_rejection, final_radar_capture


class CenterRadarTests(unittest.TestCase):
    def setUp(self):
        self.world=dict(world=100,persistent_level=200,controller=300,player_actor=400,player=(1,2,3))

    def valid(self, **changes):
        values=dict(actors=[1,2],final_actors=[1,2],world=self.world,final_world=self.world,
            rejected=[],read_errors=0,wanted=None)
        values.update(changes)
        return radar_complete(**values)

    def test_full_radar_independent_of_broker_missing_ids(self):
        self.assertTrue(self.valid())
        self.assertFalse(self.valid(wanted={99}))

    def test_live_type_six_is_an_identity_without_price_admission(self):
        self.assertIsNone(binding_rejection(1233157420,100,'Leprechaun',6,82000,148000))
        self.assertEqual(binding_rejection(7,100,'Unknown',7,82000,148000),'unsupported_kiosk')

    def test_retry_keeps_one_complete_capture_and_never_merges_partials(self):
        samples=iter([{'radar_complete':False,'bindings':[1]}, {'radar_complete':True,'bindings':[2]}])
        waits=[]
        self.assertEqual(final_radar_capture(7,lambda _:next(samples),waits.append)['bindings'],[2])
        self.assertEqual(waits,[.25])
        samples=iter([{'radar_complete':False,'bindings':[n]} for n in range(3)])
        self.assertFalse(final_radar_capture(7,lambda _:next(samples),lambda _:None)['radar_complete'])

    def test_partial_changed_world_or_movement_retains_history(self):
        for changes in [dict(final_actors=[1]),dict(rejected=[{'reason':'missing_name'}]),
                dict(read_errors=1),dict(final_world=None),
                dict(final_world={**self.world,'player_actor':401}),
                dict(final_world={**self.world,'player':(20,2,3)})]:
            with self.subTest(changes=changes): self.assertFalse(self.valid(**changes))

    def test_silent_diagnostic_read_default_cannot_prove_absence(self):
        class FailedClient:
            def read(self, pid, address, size): raise OSError('unreadable')
        mem=CaptureMemory(FailedClient(),7)
        self.assertEqual(mem.u64(100),0)
        self.assertEqual(mem.read_errors,1)
        self.assertFalse(self.valid(read_errors=mem.read_errors))


if __name__=='__main__': unittest.main()
