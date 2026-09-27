import json
from pathlib import Path
import tempfile
import unittest
from city_maps import load_navigation
from cycle_plan import price_navigation_data
from walk_follow import temple_gate_direct_allowed, shop_lookahead
from walk_geometry import Navigation


class CityMapTests(unittest.TestCase):
    def test_giran_profile_loads_its_geometry_and_gate_rules(self):
        data=load_navigation('Giran')
        self.assertEqual(data['city'],'Giran')
        self.assertEqual(len(data['navigationRules']['gates']),3)
        self.assertTrue(data['ground'])
        nav=Navigation(data)
        from shapely.geometry import Point
        for trader in ((83163,150520),(81728,146766),(84501,148000),(80692,148000)):
            self.assertTrue(nav.region.covers(Point(trader)))

    def test_unknown_city_cannot_silently_use_giran(self):
        with self.assertRaisesRegex(RuntimeError,'No validated navigation map'):
            load_navigation('Gludio')

    def test_other_city_has_no_giran_wall_or_gate_exceptions(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);city=root/'Other';city.mkdir()
            (root/'cities.json').write_text(json.dumps({'Other':'Other/city.json'}))
            (city/'city.json').write_text(json.dumps({'schema':1,'city':'Other',
                'validatedCapsule':[9,23],'coordinateOrigin':[0,0],'navigation':'navigation.json'}))
            obstacle={'name':'Giran_CH_roof [capsule band]','rings':[[[500,500],[600,500],[600,600],[500,600]]]}
            (city/'navigation.json').write_text(json.dumps({'extent':[0,1000,0,1000],'obstacles':[obstacle],'unknown':[]}))
            data=load_navigation('Other',root)
            self.assertEqual(data['hardExclusions'],[])
            self.assertEqual(price_navigation_data(data)['obstacles'],[obstacle])
            nav=Navigation(data)
            self.assertTrue(nav.clear((100,100),(200,200)))
            self.assertFalse(nav.clear((550,550),(550,550)))
            self.assertEqual(shop_lookahead((83730,148625,0),None,210,data['navigationRules']),210)
            self.assertFalse(temple_gate_direct_allowed((83730,148625,0),(83820,148625),
                {'outer':'Giran_CH_front_body','normal':[-1,0,0],'point':[83772,148625,0]},
                data['navigationRules']))


if __name__=='__main__': unittest.main()
