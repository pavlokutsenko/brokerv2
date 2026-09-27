import sys
import unittest
import tempfile
import json
from pathlib import Path
from unittest.mock import patch, MagicMock

ROOT=Path(__file__).resolve().parents[2]/'tools'/'BrokerWorker'/'src'
sys.path[:0]=[str(ROOT/'diagnostics'),str(ROOT/'client')]
from broker_response_policy import validated_responses
import collect_full_broker_inventory as collect


def reply(item, count=1, copied=1, rows=None):
    return {'arg0':item,'arg1':1,'count':count,'copied_count':copied,
            'rows':rows if rows is not None else [{'object_id':42,'amount':123,'item_id':item}]}


class PartialBrokerTests(unittest.TestCase):
    def test_partial_preserves_whole_correlated_answers(self):
        valid,warnings=validated_responses([reply(1),reply(2,3,1),reply(99)],{(1,1),(2,1),(3,1)})
        self.assertEqual(valid,[reply(1)])
        self.assertTrue(any('received=1/3' in w for w in warnings))
        self.assertTrue(any('truncated' in w for w in warnings))
        self.assertTrue(any('unexpected' in w for w in warnings))

    def test_empty_answer_is_valid_but_missing_answer_is_not_empty(self):
        valid,warnings=validated_responses([reply(1,0,0,[])],{(1,1),(2,1)})
        self.assertEqual(len(valid),1)
        self.assertEqual(valid[0]['rows'],[])
        self.assertTrue(warnings)

    def test_duplicate_and_decoder_mismatch_cannot_claim_full_epoch(self):
        valid,warnings=validated_responses([reply(1),reply(1),reply(2,1,1,[])],{(1,1),(2,1)})
        self.assertEqual(len(valid),1)
        self.assertEqual(len(warnings),3)

    def test_complete_valid_answers_have_no_warning(self):
        valid,warnings=validated_responses([reply(2),reply(1)],{(1,1),(2,1)})
        self.assertEqual(len(valid),2)
        self.assertEqual(warnings,[])

    def test_response_timeout_finalizes_received_rows_as_partial_without_retry(self):
        with tempfile.TemporaryDirectory() as folder:
            output=Path(folder)/'broker.json'
            before={'pid':7,'at':'2026-09-26T00:00:00+00:00','bindings':[{'object_id':42,'name':'Known'}]}
            response={**reply(1),'sequence':1,'function_name':'BrokerTradersByItem'}
            with patch.object(sys,'argv',['collect','--store-types','1','--output',str(output)]), \
                 patch.object(collect,'load_state',return_value={'pid':7,'history_depth':32}), \
                 patch('bind_broker_actors.capture',return_value=before), \
                 patch.object(collect,'send_market',return_value={'function_name':'BrokerMarketItemsList','count':2,'copied_count':2,'rows':[1,2]}), \
                 patch.object(collect,'quiet_search') as query, \
                 patch.object(collect,'Lu4MemoryClient',return_value=MagicMock()), \
                 patch.object(collect,'read_capture',side_effect=[{'sequence':0},{'sequence':1}]), \
                 patch.object(collect,'wait_for_sequence',side_effect=TimeoutError('broker sequence stopped at 1, expected 2')), \
                 patch.object(collect,'read_history',return_value=[response]), \
                 patch.object(collect,'publish'):
                self.assertEqual(collect.main(),0)
            saved=json.loads(output.read_text())
            self.assertFalse(saved['complete'])
            self.assertEqual(saved['summary']['market_item_responses'],1)
            self.assertEqual(saved['summary']['market_item_requests'],2)
            self.assertEqual(saved['rows'][0]['amount'],123)
            self.assertEqual(saved['rows'][0]['trader_name'],'Known')
            self.assertEqual(query.call_count,2)
            self.assertTrue(saved['warnings'])
