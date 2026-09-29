import unittest
from city_maps import load_navigation
from collection_zone import in_collection_zone


class CollectionZoneTests(unittest.TestCase):
    def setUp(self): self.zone=load_navigation('Giran')['collectionZone']

    def test_far_outliers_excluded_but_temple_and_upper_extension_included(self):
        for point in ((81728,146766),(83163,150520),(81500,147100),(85000,148000)):
            self.assertFalse(in_collection_zone(self.zone,*point),point)
        for point in ((82413,148117),(84082,148612),(83657,147149),(80888,147725),(80747,149698)):
            self.assertTrue(in_collection_zone(self.zone,*point),point)

    def test_boundary_is_included_and_other_city_has_no_giran_rule(self):
        for point in self.zone['polygon']:
            self.assertTrue(in_collection_zone(self.zone,*point))
        self.assertTrue(in_collection_zone(None,100,100))
        self.assertFalse(in_collection_zone(self.zone,float('nan'),148000))

    def test_northeast_extension_includes_the_two_missed_live_shops_only(self):
        for point in ((84693,147700),(84662,147581)):
            self.assertTrue(in_collection_zone(self.zone,*point),point)
        self.assertFalse(in_collection_zone(self.zone,84693,148000))
        self.assertFalse(in_collection_zone(self.zone,84781,147700))
